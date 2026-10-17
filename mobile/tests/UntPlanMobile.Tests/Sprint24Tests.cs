using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using UntPlanMobile.Api;

namespace UntPlanMobile.Tests;

/// <summary>
/// Sprint 24 (Parität zur iOS-App): Alphabet-Register der Planauswahl, Info-Zeichen nicht invertiert, Einzelstunden in
/// Doppel-/Blockstunden (Vertrag Einzelstunde) und das neue Mitteilungsmodul (Empfängerauswahl, Editor, Detail mit Anhängen).
/// </summary>
public class Sprint24Tests
{
    private static string Datei(params string[] teile) => File.ReadAllText(Path.Combine(new[] { Umgebung.Wurzel(), "mobile", "src", "UntPlanMobile" }.Concat(teile).ToArray()));

    private static string AppJs => Datei("wwwroot", "assets", "js", "app.js");

    private static string Abschnitt(string js, string name)
    {
        var beginn = js.IndexOf($"/* {name}-BEGINN", StringComparison.Ordinal);
        var ende = js.IndexOf($"/* {name}-ENDE */", StringComparison.Ordinal);
        Assert.True(beginn > 0 && ende > beginn, $"Abschnitt {name} fehlt");
        return js[beginn..ende];
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

    /// <summary>Führt einen Abschnitt aus app.js mit Prüfungen in Node aus (ohne Node: nur die statische Prüfung).</summary>
    private static void MitNode(string abschnitt, string pruefungen)
    {
        var node = Node();
        if (node is null) return;
        var skript = Path.Combine(Path.GetTempPath(), "untplan-s24-" + Guid.NewGuid().ToString("N") + ".js");
        File.WriteAllText(skript, "const window = {};\n" + abschnitt + """

            const pruefe = (ist, soll) => { const a = JSON.stringify(ist), b = JSON.stringify(soll); if (a !== b) { console.error(a + ' statt ' + b); process.exit(1); } };
            """ + pruefungen + "\nconsole.log('ok');\n");
        try
        {
            using var p = Process.Start(new ProcessStartInfo(node, skript) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
            var aus = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
            Assert.True(p.ExitCode == 0, aus);
            Assert.Contains("ok", aus);
        }
        finally { File.Delete(skript); }
    }

    // ------------------------------------------------------------------------------------------ Planauswahl

    [Fact]
    public void Alphabet_Register_springt_zum_naechsten_vorhandenen_Buchstaben()
    {
        var js = AppJs;
        var abschnitt = Abschnitt(js, "REGISTER");
        Assert.Contains("pkRegister(letters", js);
        Assert.Contains("pointermove", js);
        MitNode(abschnitt, """
            const r = window.UntPlanRegister;
            pruefe(r.alphabet.length, 27);
            pruefe(r.buchstabe('Äpfel'), 'A');
            pruefe(r.buchstabe('müller'), 'M');
            pruefe(r.buchstabe('5a'), '#');
            pruefe(r.buchstabe(''), '#');
            pruefe(r.ziel('C', ['A', 'D', 'M']), 'D');
            pruefe(r.ziel('A', ['A', 'D', 'M']), 'A');
            pruefe(r.ziel('Z', ['A', 'D', 'M']), 'M');
            pruefe(r.ziel('#', ['#', 'B']), '#');
            pruefe(r.ziel('Q', []), null);
            """);
    }

    // ------------------------------------------------------------------------------------------ Info-Zeichen

    [Fact]
    public void Info_Zeichen_wie_im_Original()
    {
        // Sprint 26: auf der Kachel wie im Original – abgerundetes Feld, graue Scheibe mit ausgespartem „i“ (eigene Formen,
        // keine Farbumkehr per Filter); die Stundendetails behalten ihr Linienzeichen
        var js = AppJs;
        var beginn = js.IndexOf("const KACHEL_INFO", StringComparison.Ordinal);
        var info = js[beginn..js.IndexOf('\n', beginn)];
        Assert.Contains("ticon__feld", info);
        Assert.Contains("ticon__scheibe", info);
        Assert.DoesNotContain("evenodd", info);
        var css = Datei("wwwroot", "assets", "css", "app.css");
        Assert.DoesNotContain("invert(", css);
        Assert.Contains(".card .ticon--info .ticon__scheibe{ fill:#818e9c; }", css);
        Assert.Contains("html.is-dark .card .ticon--info .ticon__feld{ fill:#313d44; }", css);
        Assert.DoesNotContain("dico--info\" aria-hidden=\"true\"><use href=\"#ic-tt-info-icon\"", js);
    }

    // ------------------------------------------------------------------------------------------ Einzelstunden

    [Fact]
    public void Teilauswahl_bildet_Block_Einzelstunden_und_geerbten_Eintrag_ab()
    {
        var roh = JsonNode.Parse("""
            { "eintrag": { "thema": "Übung", "einzelstunde": 4 }, "darf": true, "einzelstunde": 4,
              "blockEintrag": { "thema": "Brüche: Einführung", "notizSchueler": "Zirkel", "notiz": "intern", "online": true },
              "block": { "von": 3, "bis": 4, "beginn": "09:35", "ende": "11:05" },
              "einzelstunden": [ { "stunde": 3, "beginn": "09:35", "ende": "10:20", "eintrag": false }, { "stunde": 4, "beginn": "10:20", "ende": "11:05", "eintrag": true } ] }
            """);
        var t = KlassenbuchApi.Teilauswahl(roh, 4, kollegium: true);
        Assert.Equal(4, t["part"]!.GetValue<int>());
        Assert.Equal(3, t["block"]!["from"]!.GetValue<int>());
        Assert.Equal("11:05", t["block"]!["end"]!.GetValue<string>());
        Assert.Equal(new[] { 3, 4 }, t["parts"]!.AsArray().Select(x => x!["lesson"]!.GetValue<int>()));
        Assert.True(t["parts"]![1]!["own"]!.GetValue<bool>());
        Assert.Equal("Brüche: Einführung", t["inherited"]!["lehrstoff"]!.GetValue<string>());
        Assert.Equal("intern", t["inherited"]!["notes_teachers"]!.GetValue<string>());

        // Ganzer Block: nichts geerbt; Schüler*innen sehen keine internen Notizen
        var ganz = KlassenbuchApi.Teilauswahl(JsonNode.Parse("""{ "einzelstunde": null, "blockEintrag": { "thema": "X", "notiz": "intern" } }"""), null, kollegium: false);
        Assert.Null(ganz["part"]);
        Assert.Null(ganz["inherited"]);
        var schueler = KlassenbuchApi.Teilauswahl(roh, 4, kollegium: false);
        Assert.Equal("", schueler["inherited"]!["notes_teachers"]!.GetValue<string>());
    }

    [Fact]
    public void Oberflaeche_schickt_die_Einzelstunde_mit()
    {
        var js = AppJs;
        Assert.Contains("p.einzelstunde = String(c._teil)", js);
        Assert.Contains("function renderDetailTeil()", js);
        Assert.Contains("Ganzer Block", js);
        Assert.Contains("id=\"detailTeil\"", Datei("Vorlagen", "index.html"));
    }

    /// <summary>
    /// Ende zu Ende: In einer Doppelstunde gilt der Lehrstoff einer Einzelstunde nur für diese Stunde; der Block behält seinen,
    /// die Einzelstunde zeigt ihn geerbt an.
    /// </summary>
    [Fact]
    public async Task Lehrstoff_einer_Einzelstunde_bleibt_bei_dieser_Stunde()
    {
        await using var u = await Umgebung.StartenAsync();
        JsonNode? block = null;
        HttpClient? c = null;
        foreach (var login in new[] { "sne", "fis" })
        {
            c = await u.AngemeldetAsync(login);
            var woche = await Umgebung.HolenAsync(c, "/api/mobile?action=week&date=2026-10-14&type=mine");
            block = woche["days"]!.AsObject().SelectMany(t => t.Value!.AsArray())
                .FirstOrDefault(k => (k!["span"]?.GetValue<int>() ?? 1) > 1 && k["unterricht"] is JsonValue && k["blockType"]?.GetValue<string>() is not ("cancelled" or "event"));
            if (block != null) break;
        }
        Assert.NotNull(block);
        var datum = block!["date"]!.GetValue<string>();
        var von = block["lesson"]!.GetValue<int>();
        var zweite = von + 1;
        var basis = new Dictionary<string, string>
        {
            ["date"] = datum, ["lesson"] = von.ToString(), ["class"] = block["class"]?.GetValue<string>() ?? "", ["unterricht"] = block["unterricht"]!.ToString(),
            ["span"] = block["span"]!.ToString(),
        };
        var einzel = new Dictionary<string, string>(basis) { ["lesson"] = zweite.ToString(), ["einzelstunde"] = zweite.ToString() };
        einzel.Remove("span");

        await Umgebung.SendenAsync(c!, "/api/classbook", new Dictionary<string, string>(basis) { ["action"] = "save", ["field"] = "lehrstoff", ["value"] = "Block-Thema" });
        await Umgebung.SendenAsync(c!, "/api/classbook", new Dictionary<string, string>(einzel) { ["action"] = "save", ["field"] = "lehrstoff", ["value"] = "Nur zweite Stunde" });

        var q = (Dictionary<string, string> d) => string.Join("&", d.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}"));
        var ganz = await Umgebung.HolenAsync(c!, "/api/classbook?action=all&" + q(basis));
        Assert.Equal("Block-Thema", ganz["data"]!["lehrstoff"]!.GetValue<string>());
        Assert.Null(ganz["lessonPart"]!["part"]);
        var teil = await Umgebung.HolenAsync(c!, "/api/classbook?action=all&" + q(einzel));
        Assert.Equal("Nur zweite Stunde", teil["data"]!["lehrstoff"]!.GetValue<string>());
        Assert.Equal(zweite, teil["lessonPart"]!["part"]!.GetValue<int>());
        Assert.Equal("Block-Thema", teil["lessonPart"]!["inherited"]!["lehrstoff"]!.GetValue<string>());
        Assert.Contains(teil["lessonPart"]!["parts"]!.AsArray(), p => p!["lesson"]!.GetValue<int>() == zweite && p["own"]!.GetValue<bool>());
    }

    // ------------------------------------------------------------------------------------------ Mitteilungen

    [Fact]
    public void Mitteilung_und_Personen_werden_fuer_die_Oberflaeche_aufbereitet()
    {
        Assert.Equal("N", MitteilungenApi.Initialen("NEU"));
        Assert.Equal("MF", MitteilungenApi.Initialen("Michael Fischer"));
        Assert.Equal("?", MitteilungenApi.Initialen("  "));

        var m = MitteilungenApi.Eintrag((JsonObject)JsonNode.Parse("""
            { "id": 87, "absenderId": 14, "absender": "NEU", "absenderRolle": "Lehrkraft", "betreff": "BSO2", "text": "Liebe Kolleginnen",
              "erstellt": "2026-01-19 07:24", "wichtig": false, "gelesen": null, "empfaenger": ["ABB"], "antwortErlaubt": false,
              "anhaenge": [ { "id": 12, "name": "OpenPeriod.pdf", "typ": "application/pdf", "groesse": 48213 } ] }
            """)!, "posteingang");
        Assert.Equal("N", m["initials"]!.GetValue<string>());
        Assert.False(m["read"]!.GetValue<bool>());
        Assert.False(m["replyAllowed"]!.GetValue<bool>());
        Assert.False(m["canReply"]!.GetValue<bool>());
        Assert.Equal(1, m["attachmentCount"]!.GetValue<int>());
        Assert.Equal("application/pdf", m["attachments"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("benutzer:14", m["senderToken"]!.GetValue<string>());

        var p = MitteilungenApi.Personen((JsonObject)JsonNode.Parse("""
            { "typ": "kollegium",
              "personen": [ { "id": "benutzer:5", "name": "ABB", "anzeigename": "Anna Abbott", "kuerzel": "ABB", "initialen": "A", "kategorie": "lehrkraefte",
                              "rollen": [ { "art": "lehrkraft", "text": "Lehrkraft" }, { "art": "klassenlehrkraft", "text": "Klassenlehrkraft | DTI1A", "klassen": ["DTI1A"] } ],
                              "klassen": ["DTI1A"], "abteilungen": ["Technik"] } ],
              "gruppen": [],
              "filter": { "listen": [ { "id": "liste:3", "name": "Fachschaft Mathe", "mitglieder": ["benutzer:5"] } ], "klassen": ["DTI1A"], "abteilungen": ["Technik"] } }
            """)!, "kollegium");
        var person = p["items"]![0]!;
        Assert.Equal("benutzer:5", person["token"]!.GetValue<string>());
        Assert.Equal("A", person["initials"]!.GetValue<string>());
        Assert.Equal(new[] { "Lehrkraft", "Klassenlehrkraft | DTI1A" }, person["roles"]!.AsArray().Select(r => r!["text"]!.GetValue<string>()));
        Assert.Equal("liste:3", p["lists"]![0]!["token"]!.GetValue<string>());
        Assert.Equal(new[] { "Technik" }, p["departments"]!.AsArray().Select(x => x!.GetValue<string>()));
    }

    [Fact]
    public void Empfaengerfilter_Ausgewaehlt_Liste_Klasse_Abteilung_und_Suche()
    {
        var js = AppJs;
        MitNode(Abschnitt(js, "TO"), """
            const f = window.UntPlanEmpfaenger.filtern;
            const leute = [
              { token:'benutzer:1', label:'ABB', roles:[{ text:'Lehrkraft' }, { text:'Klassenlehrkraft | DTI1A' }], classes:['DTI1A'], departments:['Technik'] },
              { token:'benutzer:2', label:'AJO', roles:[{ text:'Lehrkraft' }], classes:['FPI2B'], departments:['Pflege'] },
              { token:'benutzer:3', label:'BAD', fullName:'Berta Bader', roles:[{ text:'Schulleitung' }], classes:[], departments:[] } ];
            const ids = l => l.map(p => p.token);
            const leer = { ausgewaehlt:false, liste:null, klasse:'', abteilung:'', q:'' };
            pruefe(ids(f(leute, leer, new Set())), ['benutzer:1', 'benutzer:2', 'benutzer:3']);
            pruefe(ids(f(leute, { ...leer, ausgewaehlt:true }, new Set(['benutzer:2']))), ['benutzer:2']);
            pruefe(ids(f(leute, { ...leer, liste:{ members:['benutzer:3', 'liste:9'] } }, new Set())), ['benutzer:3']);
            pruefe(ids(f(leute, { ...leer, klasse:'DTI1A' }, new Set())), ['benutzer:1']);
            pruefe(ids(f(leute, { ...leer, abteilung:'Pflege' }, new Set())), ['benutzer:2']);
            pruefe(ids(f(leute, { ...leer, q:'bader' }, new Set())), ['benutzer:3']);
            pruefe(ids(f(leute, { ...leer, q:'dti1a' }, new Set())), ['benutzer:1']);
            """);
        var html = Datei("Vorlagen", "index.html");
        Assert.Contains("An wen soll die Mitteilung gesendet werden?", html);
        Assert.Contains("Kolleg*innen", html);
        Assert.Contains("Individuell", html);
        Assert.Contains("id=\"sheetTo\"", html);
        Assert.Contains("id=\"msgOptReceipt\"", html);
        Assert.Contains("id=\"msgOptNoReply\"", html);
        Assert.Contains("capture=\"environment\"", html);
    }

    /// <summary>
    /// Ende zu Ende über UntPlanMobile: Empfängerauswahl, Senden mit Anhang (über einen Entwurf), Lesebestätigung und
    /// „Antwort nicht erlaubt“, Detail mit Anhang, Download des Anhangs, gelesen markieren.
    /// </summary>
    [Fact]
    public async Task Mitteilung_mit_Anhang_senden_lesen_und_herunterladen()
    {
        await using var u = await Umgebung.StartenAsync();
        var fis = await u.AngemeldetAsync("fis");
        var sne = await u.AngemeldetAsync("sne");

        // Empfängerauswahl „Kolleg*innen“: SNE steht darin
        var leute = await Umgebung.HolenAsync(fis, "/api/messages?action=people&type=kollegium");
        var sneEintrag = leute["items"]!.AsArray().FirstOrDefault(p => p!["token"]!.GetValue<string>().StartsWith("benutzer:") &&
            (p["label"]!.GetValue<string>().Contains("SNE") || p["fullName"]?.GetValue<string>() == "Julia Schneider" || p["label"]!.GetValue<string>().Contains("Schneider")));
        Assert.NotNull(sneEintrag);
        var token = sneEintrag!["token"]!.GetValue<string>();
        Assert.False(string.IsNullOrEmpty(sneEintrag["initials"]!.GetValue<string>()));

        // Senden mit Anhang
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Eintragungen ins EKB"), "subject" },
            { new StringContent("Liebe Kolleginnen,\nes fehlen ein paar Einträge."), "body" },
            { new StringContent($"[\"{token}\"]"), "recipients" },
            { new StringContent("1"), "read_receipt" },
            { new StringContent("1"), "no_reply" },
        };
        var pdf = new ByteArrayContent("%PDF-1.4 Test"u8.ToArray());
        pdf.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(pdf, "attachments[]", "OpenPeriod_20260119_0722.pdf");
        var gesendet = await Umgebung.JsonAsync(await fis.PostAsync("/api/messages?action=send", form));
        Assert.True(gesendet["ok"]!.GetValue<bool>());

        // Posteingang von SNE: Absender, Initialen, Büroklammer
        var liste = await Umgebung.HolenAsync(sne, "/api/messages?action=list&box=inbox");
        var m = liste["items"]!.AsArray().First(x => x!["subject"]!.GetValue<string>() == "Eintragungen ins EKB")!;
        Assert.False(m["read"]!.GetValue<bool>());
        Assert.Equal(1, m["attachmentCount"]!.GetValue<int>());
        Assert.True(m["readReceipt"]!.GetValue<bool>());
        Assert.False(string.IsNullOrEmpty(m["initials"]!.GetValue<string>()));

        // Detail: Anhang, danach gelesen
        var id = m["id"]!.GetValue<int>();
        var detail = (await Umgebung.HolenAsync(sne, $"/api/messages?action=detail&id={id}"))["item"]!;
        Assert.Equal("OpenPeriod_20260119_0722.pdf", detail["attachments"]![0]!["name"]!.GetValue<string>());
        var nachher = await Umgebung.HolenAsync(sne, "/api/messages?action=list&box=inbox");
        Assert.True(nachher["items"]!.AsArray().First(x => x!["id"]!.GetValue<int>() == id)!["read"]!.GetValue<bool>());

        // Download des Anhangs
        var anhangId = detail["attachments"]![0]!["id"]!.GetValue<int>();
        var datei = await sne.GetAsync($"/api/messages?action=attachment&id={anhangId}");
        Assert.Equal(HttpStatusCode.OK, datei.StatusCode);
        Assert.Equal("%PDF-1.4 Test", await datei.Content.ReadAsStringAsync());
        Assert.Equal("application/pdf", datei.Content.Headers.ContentType?.MediaType);

        // Gesendet bei FIS
        var raus = await Umgebung.HolenAsync(fis, "/api/messages?action=list&box=sent");
        Assert.Contains(raus["items"]!.AsArray(), x => x!["subject"]!.GetValue<string>() == "Eintragungen ins EKB");

        // Ohne Anhang direkt senden, Antwort-Vorschlag und Löschen
        await Umgebung.SendenAsync(sne, "/api/messages?action=send", new Dictionary<string, string>
        {
            ["subject"] = "Kurz", ["body"] = "Hallo", ["recipients"] = $"[\"benutzer:{m["senderToken"]!.GetValue<string>().Split(':')[1]}\"]",
        });
        var fisEingang = await Umgebung.HolenAsync(fis, "/api/messages?action=list&box=inbox");
        var kurz = fisEingang["items"]!.AsArray().First(x => x!["subject"]!.GetValue<string>() == "Kurz")!;
        Assert.True(kurz["canReply"]!.GetValue<bool>());
        var vorschlag = await Umgebung.HolenAsync(fis, $"/api/messages?action=reply&id={kurz["id"]}");
        Assert.NotEmpty(vorschlag["recipients"]!.AsArray());
        await Umgebung.SendenAsync(fis, "/api/messages", new Dictionary<string, string> { ["action"] = "delete", ["id"] = kurz["id"]!.ToString() });
        Assert.DoesNotContain((await Umgebung.HolenAsync(fis, "/api/messages?action=list&box=inbox"))["items"]!.AsArray(), x => x!["subject"]!.GetValue<string>() == "Kurz");
    }
}
