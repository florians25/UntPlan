using System.Net;
using System.Text.Json.Nodes;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Stundenkarten (Sprint 26): Online-Unterricht ohne Raumlabel, Zusatzstunde mit Badge statt Vertretungstext, Verlegung mit
/// Richtung („von“/„nach“), Pausenaufsichten mit Label hinter dem Ort, Schalter „Findet online statt“, Pfeil beim Lehrerwechsel
/// und die macOS-Korrekturen (nur mit html.is-mac).
/// </summary>
public class KartenDarstellungTests
{
    private static string Datei(params string[] teile)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "src", "UntPlanWeb", "wwwroot" }.Concat(teile).ToArray()));
    }

    private static Karte K(string status, string art, bool lehrerAbwesend = false) =>
        new("2026-10-16", 1, 1, "08:00", "08:45", status, "M", null, "Mathematik", "", new(), new(), new(), art, "", null, null, "")
        { LehrerAbwesend = lehrerAbwesend };

    // ------------------------------------------------------------------ Verlegung: Richtung

    [Fact]
    public void Verlegung_ursprungliche_Stunde_von_Zielstunde_nach()
    {
        Assert.Equal("von", K("entfall", "Verlegung").Verlegung);
        Assert.Equal("von", K("abwesend", "Verlegung").Verlegung);          // Raumplan: Stunde findet woanders statt
        Assert.Equal("nach", K("geaendert", "Verlegung").Verlegung);
        Assert.Equal("nach", K("geaendert", "Statt-Vertretung").Verlegung);
        Assert.Equal("nach", K("abwesend", "Verlegung", lehrerAbwesend: true).Verlegung);
        Assert.Null(K("geaendert", "Vertretung").Verlegung);
        Assert.Null(K("entfall", "Entfall").Verlegung);
        Assert.Null(K("klausur", "Verlegung").Verlegung);
        Assert.Null(K("regulaer", "").Verlegung);
    }

    // ------------------------------------------------------------------ Server: Zusatzstunde und Verlegung über die App-API

    [Fact]
    public async Task Zusatzstunde_ohne_Vertretungstext_und_Verlegung_mit_Richtung_in_der_App_API()
    {
        await using var s = await Testserver.StartenAsync(false);
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 60;
        var tag = paket["tage"]!.AsArray().First(t => t!["datum"]!.GetValue<string>() == "2026-10-16")!;
        JsonObject Stunde(int nr, string status, int vertretung, string text) => new()
        {
            ["stunde"] = nr, ["fach"] = "M", ["klassen"] = new JsonArray("6a"), ["lehrer"] = new JsonArray("MUE"), ["raeume"] = new JsonArray("R103"),
            ["status"] = status, ["vertretung"] = vertretung, ["text"] = text,
        };
        JsonObject Vertretung(int id, int nr, string art, string text) => new()
        {
            ["id"] = id, ["datum"] = "2026-10-16", ["stunde"] = nr, ["art"] = art, ["klassen"] = new JsonArray("6a"), ["lehrer"] = "MUE", ["vertreter"] = "MUE",
            ["fach"] = "M", ["neuesFach"] = "M", ["raum"] = "R103", ["neuerRaum"] = "R103", ["vertrVon"] = "", ["nach"] = "", ["text"] = text, ["nurLehrer"] = false,
        };
        tag["stunden"]!.AsArray().Add(Stunde(9, "zusaetzlich", 9101, "Zusatzstunde Förderung"));
        tag["stunden"]!.AsArray().Add(Stunde(10, "entfall", 9102, ""));
        tag["stunden"]!.AsArray().Add(Stunde(11, "zusaetzlich", 9103, ""));
        paket["vertretungen"]!.AsArray().Add(Vertretung(9101, 9, "Zusatzunterricht", "Zusatzstunde Förderung"));
        paket["vertretungen"]!.AsArray().Add(Vertretung(9102, 10, "Verlegung", ""));
        paket["vertretungen"]!.AsArray().Add(Vertretung(9103, 11, "Verlegung", ""));
        Assert.Equal(HttpStatusCode.OK, (await s.PaketSendenAsync(paket)).StatusCode);
        var c = await s.AlsAsync("admin");

        var plan = await Testserver.HolenAsync(c, "/api/app/plan?typ=klasse&element=6a&datum=2026-10-16");
        JsonNode Karte(int nr) => plan["karten"]!.AsArray().Single(k => k!["datum"]!.GetValue<string>() == "2026-10-16" && k["von"]!.GetValue<int>() == nr)!;

        var zusatz = Karte(9);
        Assert.True(zusatz["zusaetzlich"]!.GetValue<bool>());
        // Sprint 27: von Hand eingegebener Text bleibt (Info-Symbol), nur Standardtexte entfallen
        Assert.Equal("Zusatzstunde Förderung", zusatz["text"]!.GetValue<string>());
        Assert.Null(zusatz["verlegung"]);

        Assert.Equal("von", Karte(10)["verlegung"]!.GetValue<string>());        // ursprüngliche Stunde
        Assert.Equal("nach", Karte(11)["verlegung"]!.GetValue<string>());       // Zielstunde
        Assert.False(Karte(11)["zusaetzlich"]!.GetValue<bool>() && Karte(11)["text"]!.GetValue<string>().Length > 0);

        // reguläre Karten: kein Verlegungsfeld, Online-Feld vorhanden (kein künstliches Raumlabel)
        var regulaer = plan["karten"]!.AsArray().First(k => k!["status"]!.GetValue<string>() == "regulaer")!;
        Assert.Null(regulaer["verlegung"]);
        Assert.False(regulaer["online"]!.GetValue<bool>());
        Assert.DoesNotContain(plan["karten"]!.AsArray().SelectMany(k => k!["raeume"]!.AsArray()), r => r!["k"]!.GetValue<string>().Equals("online", StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------ Web: Karten

    [Fact]
    public void Karten_online_ohne_Raumlabel_Verlegungspfeile_Zusatzbadge()
    {
        var js = Datei("js", "plangitter.js");
        // online: kein Raumlabel, nur das Symbol online_lesson unten auf der Karte
        Assert.Contains("const istOnline = (k, typ) => !!k.online && !istEntfall(k, typ) && k.status !== 'abwesend';", js);
        Assert.Contains("const raum = istOnline(k, typ) ? '' : werte(k.raeume, entfall);", js);
        Assert.Contains("img/wu/klassenbuch/online_lesson.svg", js);
        Assert.DoesNotContain("bi-camera-video-fill", js);
        Assert.Contains("<path d=\"m20 11.011-3 1v-1", Datei("img", "wu", "klassenbuch", "online_lesson.svg"));
        // Verlegung: Richtung vom Server, Fallback Entfall = ursprüngliche Stunde
        Assert.Contains("k.verlegung === 'von' || k.verlegung === 'nach' ? k.verlegung : istEntfall(k, typ) ? 'von' : 'nach'", js);
        Assert.Contains("mk-verlegung ${richtung}", js);
        // Zusatzstunde: Badge statt Vertretungstext
        Assert.Contains("export const ZUSATZ_TEXT = 'Zusätzl. Unterrichtsstunde';", js);
        Assert.Contains("if (k.text && k.fach && !/^entf", js);   // Sprint 27: Info-Symbol auch bei Zusatzstunden mit eigenem Text
        var css = Datei("css", "karten.css");
        Assert.Contains(".mks .mk-verlegung.von { background-color: #f03e3e;", css);
        Assert.Contains("M3.5 8h9M9 4.5 12.5 8 9 11.5", css);    // Pfeil nach rechts (ursprüngliche Stunde)
        Assert.DoesNotContain("M12.5 8h-9M7 4.5 3.5 8 7 11.5", css);   // Sprint 27: auch die Zielstunde mit Pfeil nach rechts
        // Zeilen etwas enger
        Assert.Contains(".std .z1 { margin-bottom: 0; }", css);
        Assert.Contains(".std .z2 + .z2 { margin-top: 0; }", css);
        Assert.Contains("font-variant-numeric: lining-nums;", css);
    }

    [Fact]
    public void Pausenaufsicht_Label_hinter_dem_Ort_ohne_abwesende_Lehrkraft_und_ohne_Verblassen()
    {
        var js = Datei("js", "plangitter.js");
        Assert.Contains("<span class=\"auf-text\">${e(ort)}${label ? ' ' + label : ''}</span>", js);
        Assert.Contains("<span class=\"lbl lbl-auf\" data-hover=\"${e(alt ? wechsel(alt, ersatz) : '+ ' + ersatz)}\"", js);
        Assert.DoesNotContain("auf-alt", js);
        Assert.Contains("class=\"aufsicht u-hover-bereich", js);
        var css = Datei("css", "karten.css");
        Assert.Contains(".aufsicht.vergangen, .aufsicht.vergangen.vertretung, .aufsicht.vergangen.vertreten { opacity: 1; filter: none; }", css);
    }

    // ------------------------------------------------------------------ Web: Stundendetails

    [Fact]
    public void Stundendetails_Pfeil_beim_Lehrer_Schalter_online_und_Zusatzbadge()
    {
        var sd = Datei("js", "stundendetails.js");
        Assert.Contains("const zwischen = `<span class=\"sd-pfeil${lehrkraft ? ' sd-pfeil-lehrer' : ''}\">→</span>`;", sd);
        Assert.DoesNotContain("sd-strich", sd);
        Assert.Contains("<label class=\"sd-schalter\" for=\"sd-online\">", sd);
        Assert.Contains("<span class=\"sd-schalter-spur\" aria-hidden=\"true\"></span>", sd);
        Assert.DoesNotContain("form-switch", sd);
        Assert.Contains("k.zusaetzlich ? 'Zusätzl. Unterrichtsstunde' : ''", sd);
        Assert.Contains("const PLUS_SYM = '<svg class=\"sd-plus-sym\"", sd);
        Assert.DoesNotContain("<i class=\"bi bi-plus-lg\"></i>", sd);
        var css = Datei("css", "karten.css");
        Assert.Contains(".sd-schalter input:checked + .sd-schalter-spur { background: var(--u-akzent); }", css);
        Assert.Contains(".sd-schalter input:checked + .sd-schalter-spur::after { transform: translateX(16px); }", css);
        Assert.Contains(".sd-plus-sym { width: 16px; height: 16px; display: block; fill: none; stroke: currentColor; stroke-width: 1.5; stroke-linecap: round; }", css);
    }

    // ------------------------------------------------------------------ macOS-Korrekturen nur mit html.is-mac

    [Fact]
    public void MacOS_Korrekturen_nur_mit_Plattformklasse()
    {
        var plattform = Datei("js", "plattform.js");
        Assert.Contains("document.documentElement.classList.add('is-mac')", plattform);
        Assert.Contains("navigator.userAgentData", plattform);
        var index = Datei("index.html");
        Assert.Contains("<link rel=\"stylesheet\" href=\"css/karten.css\">\n  <script src=\"js/plattform.js\"></script>", index);
        Assert.True(index.IndexOf("js/plattform.js", StringComparison.Ordinal) < index.IndexOf("js/app.js", StringComparison.Ordinal));

        var css = Datei("css", "karten.css");
        var mac = css[css.IndexOf("/* ------------------------------------------------------------------ macOS", StringComparison.Ordinal)..];
        Assert.Contains("text-box: trim-both cap alphabetic;", mac);
        Assert.Contains("text-underline-offset: calc(-.5cap - var(--strich) / 2);", mac);
        Assert.Contains("text-decoration-skip-ink: none;", mac);
        // jede Regel im macOS-Abschnitt gilt nur mit der Plattformklasse – Windows bleibt unverändert
        var ohneKommentar = System.Text.RegularExpressions.Regex.Replace(mac, @"/\*.*?\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        foreach (var kopf in ohneKommentar.Split('}').Select(r => r.Contains('{') ? r[..r.LastIndexOf('{')] : "").Select(k => k.Split('{').Last().Trim())
                     .Where(k => k.Length > 0 && !k.StartsWith("@supports")))
            Assert.Contains("html:root.is-mac", kopf);
        Assert.DoesNotContain("is-mac", css[..css.IndexOf("/* ------------------------------------------------------------------ macOS", StringComparison.Ordinal)].Replace("<html class=\"is-mac\">", ""));
        Assert.DoesNotContain("text-box", Datei("css", "app.css"));
    }

    [Fact]
    public void Neue_Dateien_ohne_Fremdnamen_und_nur_Regular_Medium()
    {
        foreach (var inhalt in new[] { Datei("css", "karten.css"), Datei("js", "plattform.js"), Datei("js", "plangitter.js") })
        {
            Assert.DoesNotContain(new string(new[] { 'u', 'n', 't', 'i', 's' }), inhalt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("font-weight: 700", inhalt);
            Assert.DoesNotContain("font-weight: bold", inhalt);
        }
    }
}
