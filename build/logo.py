#!/usr/bin/env python3
"""Erzeugt alle Logodateien von UntPlan aus einer einzigen Geometrie.

Bildmarke „UP“: ein U, dessen rechter Schaft nach oben in die Schüssel eines P übergeht (UntPlan). Die Punze des P ist ein
quadratisches Zeitfenster, die aktuelle Stunde. Konstruktion im 64er-Raster mit durchgehender Strichstärke 10: linker
Schaft endet schräg angeschnitten auf Höhe der Fensterunterkante (Bewegung nach vorn), U-Bogen als Halbkreis,
P-Schüssel mit Eckradius 12,5 (innen 2,5, wie die Ecken des Zeitfensters). Aufrecht. Orange ist die Hauptfarbe, Petrol
nur Akzent (Zeitfenster, Schrift). Flach, höchstens zwei Farben, funktioniert einfarbig und invertiert. Wortmarke „UntPlan“ in Lato
Bold (OFL), enger gesetzt mit eigenen Paarabständen.

Fassungen
  freistehend   Orange, Zeitfenster Petrol (Favicon, Programmsymbol bis 24 px, Wortmarke in Petrol)
  Kachel        Orange, Marke Weiß, Zeitfenster Petrol (App-Symbole, Programmsymbol ab 32 px, Seitenleiste)

Aufruf:  python3 build/logo.py

Ausgaben
  build/            untplan.ico (16, 20, 24, 32, 40, 48, 64, 256), untplan-logo-256.png, icon.png,
                    untplan-logo.svg (Kachel), untplan-logo-voll.svg (Marke mit Wortmarke)
  Web (wwwroot/img) logo.svg (Kachel), marke.svg (freistehend), logo-voll.svg, favicon.svg, favicon-32.png,
                    apple-touch-icon.png, icon-192.png, icon-512.png, icon-maskable-512.png
  Web (wwwroot/js)  symbole.js: Konstante LOGO (Kachel als Inline-SVG)
  iOS               AppIcon.png, AppIcon-Dark.png, AppIcon-Tinted.png (je 1024 px),
                    Startlogo.imageset/Startlogo.svg (freistehend, enger viewBox)

Benötigt Pillow; für die Wortmarke zusätzlich fontTools mit Brotli (Lato Bold aus wwwroot/lib/lato).
"""
import io
import math
import os
import re
import struct

from PIL import Image, ImageDraw

HIER = os.path.dirname(os.path.abspath(__file__))
WURZEL = os.path.normpath(os.path.join(HIER, ".."))
WEB_IMG = os.path.join(WURZEL, "web", "src", "UntPlanWeb", "wwwroot", "img")
LATO_FETT = os.path.join(WURZEL, "web", "src", "UntPlanWeb", "wwwroot", "lib", "lato", "lato-latin-700-normal.woff2")
WEB_JS_SYMBOLE = os.path.join(WURZEL, "web", "src", "UntPlanWeb", "wwwroot", "js", "symbole.js")
IOS_ICON = os.path.join(WURZEL, "ios", "UntPlanMobile", "Assets.xcassets", "AppIcon.appiconset")
IOS_STARTLOGO = os.path.join(WURZEL, "ios", "UntPlanMobile", "Assets.xcassets", "Startlogo.imageset")

# ---------------------------------------------------------------- Farben

ORANGE = "#E5622D"
PETROL = "#0F3B45"
WEISS = "#FFFFFF"


def rgb(h, a=255):
    h = h.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


# ---------------------------------------------------------------- Pfade (Linien und Kreisbögen)

class Pfad:
    """Teilpfade; der erste ist Fläche, weitere sind Löcher (liegen innen, SVG: evenodd)."""

    def __init__(self):
        self.teile = []

    def neu(self, x, y):
        self.teile.append([("M", x, y)])
        return self

    def linie(self, x, y):
        self.teile[-1].append(("L", x, y))
        return self

    def bogen(self, cx, cy, r, a0, a1):
        """Kreisbogen von a0 nach a1 in Grad (0 = rechts, 90 = unten)."""
        self.teile[-1].append(("A", cx, cy, r, a0, a1))
        return self


def rrect(x, y, w, h, r):
    p = Pfad().neu(x + r, y).linie(x + w - r, y).bogen(x + w - r, y + r, r, -90, 0).linie(x + w, y + h - r)
    p.bogen(x + w - r, y + h - r, r, 0, 90).linie(x + r, y + h).bogen(x + r, y + h - r, r, 90, 180).linie(x, y + r)
    return p.bogen(x + r, y + r, r, 180, 270)


def _z(v):
    return f"{v:.3f}".rstrip("0").rstrip(".")


def svg_d(pfad):
    d = []
    for teil in pfad.teile:
        for op in teil:
            if op[0] in "ML":
                d.append(f"{op[0]}{_z(op[1])} {_z(op[2])}")
            else:
                _, cx, cy, r, a0, a1 = op
                n = max(1, math.ceil(abs(a1 - a0) / 90 - 1e-9))
                for i in range(1, n + 1):
                    a = math.radians(a0 + (a1 - a0) * i / n)
                    d.append(f"A{_z(r)} {_z(r)} 0 0 {1 if a1 > a0 else 0} {_z(cx + r * math.cos(a))} {_z(cy + r * math.sin(a))}")
        d.append("Z")
    return "".join(d)


def punkte(teil, s, dx=0.0, dy=0.0):
    pts = []
    for op in teil:
        if op[0] in "ML":
            pts.append(((op[1] + dx) * s, (op[2] + dy) * s))
        else:
            _, cx, cy, r, a0, a1 = op
            n = max(8, int(abs(a1 - a0) / 2))
            for i in range(1, n + 1):
                a = math.radians(a0 + (a1 - a0) * i / n)
                pts.append(((cx + dx + r * math.cos(a)) * s, (cy + dy + r * math.sin(a)) * s))
    return pts


# ---------------------------------------------------------------- Bildmarke

STRICH = 10            # durchgehende Strichstärke
LINKS = 8              # linker Schaft des U (x)
U_PUNZE = 8            # Innenweite des U
FENSTER_B = 9          # Punze des P = quadratisches Zeitfenster (Seitenlänge)
OBEN, UNTEN = 6, 58    # Oberkante P, Unterkante U
P_RADIUS = 12.5        # Eckradius der P-Schüssel außen (innen P_RADIUS - STRICH)
ANSATZ = 3.5           # schräger Anschnitt oben am linken U-Schaft (steigt nach rechts)
NEIGUNG = 0            # Neigung in Grad (Scherung um die Mitte der Marke); aufrecht

MITTE = LINKS + STRICH + U_PUNZE                 # mittlerer Schaft (linke Kante), gemeinsam für U und P
P_HOEHE = FENSTER_B + 2 * STRICH
RECHTS = MITTE + 2 * STRICH + FENSTER_B
RAHMEN = (LINKS, OBEN, RECHTS, UNTEN)            # Umriss vor der Neigung


def fenster_pfad():
    """Punze des P: links eckig am Schaft, rechts mit dem Innenradius der Schüssel."""
    x, y, b = MITTE + STRICH, OBEN + STRICH, FENSTER_B
    r = max(P_RADIUS - STRICH, 0.8)
    return (Pfad().neu(x, y).linie(x + b - r, y).bogen(x + b - r, y + r, r, -90, 0).linie(x + b, y + b - r)
            .bogen(x + b - r, y + b - r, r, 0, 90).linie(x, y + b))


def marke_pfad(mit_loch=True):
    """U (Halbkreisbogen, linker Schaft endet schräg auf Höhe der Fensterunterkante) und P-Schüssel um den gemeinsamen Schaft."""
    w, ro = STRICH, (STRICH * 2 + U_PUNZE) / 2
    ri, cx, cy = U_PUNZE / 2, LINKS + ro, UNTEN - ro
    u_oben = OBEN + STRICH + FENSTER_B
    R, yb = P_RADIUS, OBEN + P_HOEHE
    p = (Pfad().neu(LINKS, u_oben + ANSATZ).linie(LINKS, cy).bogen(cx, cy, ro, 180, 0).linie(MITTE + w, yb)
         .linie(RECHTS - R, yb).bogen(RECHTS - R, yb - R, R, 90, 0).linie(RECHTS, OBEN + R).bogen(RECHTS - R, OBEN + R, R, 0, -90)
         .linie(MITTE, OBEN).linie(MITTE, cy).bogen(cx, cy, ri, 0, 180).linie(LINKS + w, u_oben))
    if mit_loch:
        p.teile.append(fenster_pfad().teile[0])
    return p


def lage(inhalt=0.86, mitte=(32, 32)):
    """Affine Matrix (a, b, c, d, e, f): Marke geneigt, mittig um `mitte`, größte Ausdehnung = inhalt * 64."""
    x0, y0, x1, y1 = RAHMEN
    t = math.tan(math.radians(NEIGUNG))
    ym = (y0 + y1) / 2
    bx0, bx1 = x0 - t * (y1 - ym), x1 + t * (ym - y0)
    s = inhalt * 64 / max(bx1 - bx0, y1 - y0)
    return (s, 0, -t * s, s, s * (t * ym - (bx0 + bx1) / 2) + mitte[0], -s * ym + mitte[1])


EINS = (1, 0, 0, 1, 0, 0)


def formen(farbe=ORANGE, fenster=PETROL, inhalt=0.9):
    """Freistehende Marke: Liste (Pfad, Farbe, Matrix); fenster=None lässt die Punze offen (einfarbig)."""
    m = lage(inhalt)
    teile = [(marke_pfad(mit_loch=True), farbe, m)]
    if fenster:
        teile.append((fenster_pfad(), fenster, m))
    return teile


def kachel_formen(rund=True, inhalt=0.66, grund=ORANGE, farbe=WEISS, fenster=PETROL):
    """Marke mittig auf einer Fläche (Eckenradius 14 im 64er-Raster oder eckig)."""
    return [(rrect(0, 0, 64, 64, 14 if rund else 0.001), grund, EINS)] + formen(farbe, fenster, inhalt)


def raster(groesse, liste, k=8):
    """Rastert (Pfad, Farbe, Matrix) überabgetastet und verkleinert mit Kastenfilter."""
    n = groesse * k
    e = n / 64
    bild = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    for p, c, (a, b, cc, d, tx, ty) in liste:
        maske = Image.new("L", (n, n), 0)
        zeichne = ImageDraw.Draw(maske)
        for i, teil in enumerate(p.teile):
            pts = [((a * x + cc * y + tx) * e, (b * x + d * y + ty) * e) for x, y in punkte(teil, 1)]
            zeichne.polygon(pts, fill=255 if i == 0 else 0)
        bild = Image.composite(Image.new("RGBA", (n, n), rgb(c)), bild, maske)
    return bild.resize((groesse, groesse), Image.BOX)


def svg_teile(liste, einzug="  "):
    zeilen = []
    for p, c, m in liste:
        if m == EINS:
            t = ""
        elif m[1] == 0 and abs(m[2]) < 1e-9 and m[0] == m[3]:
            t = f' transform="translate({_z(m[4])} {_z(m[5])}) scale({_z(m[0])})"'
        else:
            t = f' transform="matrix({" ".join(_z(round(v, 4)) for v in m)})"'
        zeilen.append(f'{einzug}<path{t} fill="{c}" fill-rule="evenodd" d="{svg_d(p)}"/>')
    return "\n".join(zeilen)


def svg_bildmarke(liste, px=256, extra=""):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{px}" height="{px}" viewBox="0 0 64 64">\n'
            f'  <title>UntPlan</title>\n{extra}{svg_teile(liste)}\n</svg>\n')


def svg_startlogo():
    """Freistehende Marke wie marke.svg, aber mit engem viewBox (Startbildschirm iOS, Vektor)."""
    x0, y0, x1, y1 = RAHMEN
    t = math.tan(math.radians(NEIGUNG))
    ym = (y0 + y1) / 2
    b, h = (x1 - x0) + t * (y1 - y0), y1 - y0
    s = 64 / max(b, h)
    vb = f"{_z(32 - b * s / 2)} {_z(32 - h * s / 2)} {_z(b * s)} {_z(h * s)}"
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{_z(b * s * 4)}" height="{_z(h * s * 4)}" viewBox="{vb}">\n'
            f'  <title>UntPlan</title>\n{svg_teile(formen(inhalt=1.0))}\n</svg>\n')


# ---------------------------------------------------------------- Wortmarke (Lato Bold als Umriss)

SPERRUNG = -24                                                       # Font-Einheiten (2000 je Geviert)
UNTERSCHNITT = {"Un": -10, "nt": -20, "tP": 30, "Pl": -10, "la": -10, "an": -10}   # eigene Paarabstände


def wortmarke_pfad(text="UntPlan", hoehe=36.0):
    """SVG-Pfad der Wortmarke; Grundlinie bei y = hoehe, Versalhöhe = 0,72 * hoehe. Liefert (pfad, breite)."""
    from fontTools.pens.svgPathPen import SVGPathPen
    from fontTools.pens.transformPen import TransformPen
    from fontTools.ttLib import TTFont

    font = TTFont(LATO_FETT)
    cmap, glyphen = font.getBestCmap(), font.getGlyphSet()
    versal = font["OS/2"].sCapHeight or 1433
    s = hoehe * 0.72 / versal
    pen = SVGPathPen(glyphen, ntos=lambda v: f"{v:.2f}".rstrip("0").rstrip("."))
    x = 0.0
    for i, z in enumerate(text):
        name = cmap[ord(z)]
        glyphen[name].draw(TransformPen(pen, (s, 0, 0, -s, x * s, hoehe)))
        x += glyphen[name].width + SPERRUNG + UNTERSCHNITT.get(text[i:i + 2], 0)
    return pen.getCommands(), (x - SPERRUNG) * s


def svg_voll():
    """Marke im 64er-Raster, daneben die Wortmarke (Versalhöhe 26, auf die optische Mitte der Marke gesetzt)."""
    pfad, breite = wortmarke_pfad(hoehe=36.0)
    dx, dy = 70, 45 - 36
    gesamt = dx + breite + 4
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{gesamt * 2:.0f}" height="128" viewBox="0 0 {gesamt:.1f} 64">\n'
            f'  <title>UntPlan</title>\n{svg_teile(formen(inhalt=0.92))}\n'
            f'  <path transform="translate({dx} {dy})" fill="{PETROL}" d="{pfad}"/>\n</svg>\n')


def js_logo():
    """Kachel für UntPlanWeb (js/symbole.js, Konstante LOGO), gleiche Geometrie wie img/logo.svg."""
    teile = svg_teile(kachel_formen())
    return ('export const LOGO = `<svg class="logo" viewBox="0 0 64 64" aria-hidden="true" focusable="false">\n'
            f'{teile}\n</svg>`;')


# ---------------------------------------------------------------- ICO-Datei

def ico(bilder):
    """Mehrfach-ICO: bis 64 px als 32-Bit-DIB mit Alpha, 256 px PNG-komprimiert."""
    eintraege = []
    for b in bilder:
        w = b.size[0]
        if w >= 256:
            puffer = io.BytesIO()
            b.save(puffer, "PNG", optimize=True)
            daten = puffer.getvalue()
        else:
            kopf = struct.pack("<IiiHHIIiiII", 40, w, w * 2, 1, 32, 0, 0, 0, 0, 0, 0)
            pixel = bytearray()
            for y in range(w - 1, -1, -1):
                for x in range(w):
                    r, g, bl, a = b.getpixel((x, y))
                    pixel += bytes((bl, g, r, a))
            masken_zeile = ((w + 31) // 32) * 4
            maske = bytearray()
            for y in range(w - 1, -1, -1):
                bits = bytearray(masken_zeile)
                for x in range(w):
                    if b.getpixel((x, y))[3] == 0:
                        bits[x // 8] |= 0x80 >> (x % 8)
                maske += bits
            daten = kopf + bytes(pixel) + bytes(maske)
        eintraege.append((w, daten))
    ausgabe = struct.pack("<HHH", 0, 1, len(eintraege))
    versatz = 6 + 16 * len(eintraege)
    for w, daten in eintraege:
        ausgabe += struct.pack("<BBBBHHII", w % 256, w % 256, 0, 0, 1, 32, len(daten), versatz)
        versatz += len(daten)
    for _, daten in eintraege:
        ausgabe += daten
    return ausgabe


# ---------------------------------------------------------------- Ausgabe

def schreibe(pfad, text):
    with open(pfad, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def main():
    # Programm (Windows): bis 24 px die freistehende Marke, ab 32 px die Kachel
    frei, kachel = formen(), kachel_formen()
    bilder = [raster(g, frei) for g in (16, 20, 24)] + [raster(g, kachel) for g in (32, 40, 48, 64, 256)]
    with open(os.path.join(HIER, "untplan.ico"), "wb") as f:
        f.write(ico(bilder))
    bilder[-1].save(os.path.join(HIER, "untplan-logo-256.png"), optimize=True)
    bilder[-1].save(os.path.join(HIER, "icon.png"), optimize=True)
    schreibe(os.path.join(HIER, "untplan-logo.svg"), svg_bildmarke(kachel))
    voll = svg_voll()
    schreibe(os.path.join(HIER, "untplan-logo-voll.svg"), voll)

    # Web
    if os.path.isdir(WEB_IMG):
        schreibe(os.path.join(WEB_IMG, "logo.svg"), svg_bildmarke(kachel))
        schreibe(os.path.join(WEB_IMG, "marke.svg"), svg_bildmarke(frei))
        schreibe(os.path.join(WEB_IMG, "favicon.svg"), svg_bildmarke(frei, 32))
        schreibe(os.path.join(WEB_IMG, "logo-voll.svg"), voll)
        raster(32, frei).save(os.path.join(WEB_IMG, "favicon-32.png"), optimize=True)
        eckig = kachel_formen(rund=False)
        raster(180, eckig).convert("RGB").save(os.path.join(WEB_IMG, "apple-touch-icon.png"), optimize=True)
        raster(192, kachel).save(os.path.join(WEB_IMG, "icon-192.png"), optimize=True)
        raster(512, kachel).save(os.path.join(WEB_IMG, "icon-512.png"), optimize=True)
        # maskierbar: volle Fläche, Marke in der inneren Sicherheitszone
        raster(512, kachel_formen(rund=False, inhalt=0.48)).convert("RGB").save(os.path.join(WEB_IMG, "icon-maskable-512.png"), optimize=True)
    if os.path.isfile(WEB_JS_SYMBOLE):
        with open(WEB_JS_SYMBOLE, encoding="utf-8") as f:
            js = f.read()
        js = re.sub(r"export const LOGO = `<svg.*?</svg>`;", lambda _: js_logo(), js, count=1, flags=re.S)
        schreibe(WEB_JS_SYMBOLE, js)

    # iOS (das System rundet die Ecken selbst; dunkel: Marke Orange, getönt: Marke Weiß, jeweils mit offenem Fenster auf transparentem Grund)
    if os.path.isdir(IOS_ICON):
        raster(1024, kachel_formen(rund=False)).convert("RGB").save(os.path.join(IOS_ICON, "AppIcon.png"), optimize=True)
        dunkel = kachel_formen(rund=False, farbe=ORANGE, fenster=None)[1:]
        raster(1024, dunkel).save(os.path.join(IOS_ICON, "AppIcon-Dark.png"), optimize=True)
        getoent = kachel_formen(rund=False, farbe=WEISS, fenster=None)[1:]
        raster(1024, getoent).convert("LA").save(os.path.join(IOS_ICON, "AppIcon-Tinted.png"), optimize=True)
    # iOS Startbildschirm: freistehende Marke als Vektor (Contents.json pflegt das Xcode-Projekt)
    if os.path.isdir(os.path.dirname(IOS_STARTLOGO)):
        os.makedirs(IOS_STARTLOGO, exist_ok=True)
        schreibe(os.path.join(IOS_STARTLOGO, "Startlogo.svg"), svg_startlogo())


if __name__ == "__main__":
    main()
