#!/usr/bin/env python3
"""Erzeugt alle Logodateien von UntPlan aus einer einzigen Geometrie.

Bildmarke „UP“: ein U, dessen rechter Schaft nach oben in den Bogen eines P übergeht (UntPlan). Die Punze des P ist ein
quadratisches Zeitfenster, die aktuelle Stunde. Konstruktion im 64er-Raster mit durchgehender Strichstärke 11: linker
Schaft endet auf Höhe der Punzenunterkante, U-Bogen und P-Bogen sind Halbkreise. Flach, höchstens zwei Farben
(Terrakotta, Anthrazit), funktioniert einfarbig und invertiert. Wortmarke „UntPlan“ in Lato Bold (OFL), leicht enger
gesetzt.

Fassungen
  freistehend   Terrakotta, Zeitfenster Anthrazit (Favicon, Programmsymbol bis 24 px, Wortmarke)
  Kachel        Anthrazit, Marke Weiß, Zeitfenster Terrakotta (App-Symbole, Programmsymbol ab 32 px, Seitenleiste)

Aufruf:  python3 build/logo.py

Ausgaben
  build/            untplan.ico (16, 20, 24, 32, 40, 48, 64, 256), untplan-logo-256.png, icon.png,
                    untplan-logo.svg (Kachel), untplan-logo-voll.svg (Marke mit Wortmarke)
  Web (wwwroot/img) logo.svg (Kachel), marke.svg (freistehend), logo-voll.svg, favicon.svg, favicon-32.png,
                    apple-touch-icon.png, icon-192.png, icon-512.png, icon-maskable-512.png
  iOS               AppIcon.png, AppIcon-Dark.png, AppIcon-Tinted.png (je 1024 px)

Benötigt Pillow; für die Wortmarke zusätzlich fontTools mit Brotli (Lato Bold aus wwwroot/lib/lato).
"""
import io
import math
import os
import struct

from PIL import Image, ImageDraw

HIER = os.path.dirname(os.path.abspath(__file__))
WURZEL = os.path.normpath(os.path.join(HIER, ".."))
WEB_IMG = os.path.join(WURZEL, "web", "src", "UntPlanWeb", "wwwroot", "img")
LATO_FETT = os.path.join(WURZEL, "web", "src", "UntPlanWeb", "wwwroot", "lib", "lato", "lato-latin-700-normal.woff2")
IOS_ICON = os.path.join(WURZEL, "ios", "UntPlanMobile", "Assets.xcassets", "AppIcon.appiconset")

# ---------------------------------------------------------------- Farben

TERRAKOTTA = "#B8452B"
ANTHRAZIT = "#24323D"
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

STRICH = 11
FENSTER = (40, 17, 9, 9, 1.5)        # Punze des P = Zeitfenster (x, y, b, h, Radius)
RAHMEN = (6, 6, 61, 58)             # Umriss der Marke (x0, y0, x1, y1)


def marke_pfad(mit_loch=True):
    """U (Schäfte 6–17 und 29–40, Bogen um 23/41) und P-Bogen (29–61, 6–37, Halbkreis um 45,5/21,5)."""
    p = (Pfad().neu(6, 26).linie(6, 41).bogen(23, 41, 17, 180, 0).linie(40, 37).linie(45.5, 37)
         .bogen(45.5, 21.5, 15.5, 90, -90).linie(29, 6).linie(29, 41).bogen(23, 41, 6, 0, 180).linie(17, 26))
    if mit_loch:
        p.teile.append(rrect(*FENSTER).teile[0])
    return p


def formen(farbe=TERRAKOTTA, fenster=ANTHRAZIT):
    """Liste (Pfad, Farbe) im 64er-Raster; fenster=None lässt die Punze offen."""
    teile = [(marke_pfad(mit_loch=not fenster), farbe)]
    if fenster:
        teile.append((rrect(*FENSTER), fenster))
    return teile


def kachel_formen(rund=True, inhalt=0.7, grund=ANTHRAZIT, farbe=WEISS, fenster=TERRAKOTTA):
    """Marke mittig auf einer Fläche; liefert (Liste (Pfad, Farbe, Versatz, Maßstab), Eckenradius)."""
    x0, y0, x1, y1 = RAHMEN
    s = inhalt * 64 / max(x1 - x0, y1 - y0) * 0.92
    dx, dy = 32 - (x0 + x1) / 2 * s, 32 - (y0 + y1) / 2 * s
    return [(rrect(0, 0, 64, 64, 14 if rund else 0.001), grund, 0, 0, 1)] + [(p, c, dx, dy, s) for p, c in formen(farbe, fenster)]


def _mit_lage(liste):
    return [x if len(x) == 5 else (x[0], x[1], 0, 0, 1) for x in liste]


def raster(groesse, liste, k=8):
    """Rastert (Pfad, Farbe[, dx, dy, Maßstab]) überabgetastet und verkleinert mit Kastenfilter."""
    n = groesse * k
    e = n / 64
    bild = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    for p, c, dx, dy, s in _mit_lage(liste):
        maske = Image.new("L", (n, n), 0)
        d = ImageDraw.Draw(maske)
        for i, teil in enumerate(p.teile):
            pts = [((x * s + dx) * e, (y * s + dy) * e) for x, y in punkte(teil, 1)]
            d.polygon(pts, fill=255 if i == 0 else 0)
        bild = Image.composite(Image.new("RGBA", (n, n), rgb(c)), bild, maske)
    return bild.resize((groesse, groesse), Image.BOX)


def svg_teile(liste, einzug="  "):
    zeilen = []
    for p, c, dx, dy, s in _mit_lage(liste):
        t = f' transform="translate({_z(dx)} {_z(dy)}) scale({_z(s)})"' if s != 1 or dx or dy else ""
        zeilen.append(f'{einzug}<path{t} fill="{c}" fill-rule="evenodd" d="{svg_d(p)}"/>')
    return "\n".join(zeilen)


def svg_bildmarke(liste, px=256):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{px}" height="{px}" viewBox="0 0 64 64">\n'
            f'  <title>UntPlan</title>\n{svg_teile(liste)}\n</svg>\n')


# ---------------------------------------------------------------- Wortmarke (Lato Bold als Umriss)

def wortmarke_pfad(text="UntPlan", hoehe=36.0):
    """SVG-Pfad der Wortmarke; Grundlinie bei y = hoehe, Versalhöhe = 0,72 * hoehe. Liefert (pfad, breite)."""
    from fontTools.pens.svgPathPen import SVGPathPen
    from fontTools.pens.transformPen import TransformPen
    from fontTools.ttLib import TTFont

    font = TTFont(LATO_FETT)
    cmap, glyphen = font.getBestCmap(), font.getGlyphSet()
    upm = font["head"].unitsPerEm
    versal = font["OS/2"].sCapHeight or 1433
    s = hoehe * 0.72 / versal
    pen = SVGPathPen(glyphen, ntos=lambda v: f"{v:.2f}".rstrip("0").rstrip("."))
    x = 0.0
    sperrung = -0.01 * upm   # leicht enger gesetzt
    for z in text:
        name = cmap[ord(z)]
        glyphen[name].draw(TransformPen(pen, (s, 0, 0, -s, x * s, hoehe)))
        x += glyphen[name].width + sperrung
    return pen.getCommands(), (x - sperrung) * s


def svg_voll():
    """Marke im 64er-Raster, daneben die Wortmarke (Versalhöhe 26, auf die optische Mitte der Marke gesetzt)."""
    pfad, breite = wortmarke_pfad(hoehe=36.0)
    dx, dy = 76, 45 - 36
    gesamt = dx + breite + 4
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{gesamt * 2:.0f}" height="128" viewBox="0 0 {gesamt:.1f} 64">\n'
            f'  <title>UntPlan</title>\n{svg_teile(formen())}\n'
            f'  <path transform="translate({dx} {dy})" fill="{ANTHRAZIT}" d="{pfad}"/>\n</svg>\n')


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
        raster(512, kachel_formen(rund=False, inhalt=0.5)).convert("RGB").save(os.path.join(WEB_IMG, "icon-maskable-512.png"), optimize=True)

    # iOS (das System rundet die Ecken selbst; dunkel und getönt auf transparentem Grund)
    if os.path.isdir(IOS_ICON):
        raster(1024, kachel_formen(rund=False)).convert("RGB").save(os.path.join(IOS_ICON, "AppIcon.png"), optimize=True)
        dunkel = kachel_formen(rund=False, farbe="#F2F4F6", fenster="#E0694A")[1:]
        raster(1024, dunkel).save(os.path.join(IOS_ICON, "AppIcon-Dark.png"), optimize=True)
        getoent = kachel_formen(rund=False, farbe=WEISS, fenster="#9A9A9A")[1:]
        raster(1024, getoent).convert("LA").save(os.path.join(IOS_ICON, "AppIcon-Tinted.png"), optimize=True)


if __name__ == "__main__":
    main()
