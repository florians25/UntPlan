#!/usr/bin/env python3
"""Erzeugt Programmlogo, Windows-Symboldatei und Symbol-Vorschaubogen für UntPlan.

Die Zeichenanweisungen und die Palette werden direkt aus src/UntPlan.App/Symbole.cs gelesen,
damit Programmsymbole, Logo und Vorschau immer übereinstimmen. Der Interpreter bildet den
C#-Zeichner (Symbole.Zeichner) nach: 32er-Raster, Kanten auf ganze Pixel gerundet, Umriss 1 px.

Aufruf:  python3 build/logo.py            -> build/untplan.ico, build/untplan-logo.svg,
                                             build/untplan-logo-256.png, build/icon.png
         python3 build/logo.py --vorschau DATEI.png  -> zusätzlich Vorschaubogen aller Symbole

Benötigt Pillow (pip install pillow).
"""
import io
import math
import os
import re
import struct
import sys

from PIL import Image, ImageDraw, ImageFont

HIER = os.path.dirname(os.path.abspath(__file__))
QUELLE = os.path.join(HIER, "..", "src", "UntPlan.App", "Symbole.cs")


def lade_definitionen(pfad=QUELLE):
    text = open(pfad, encoding="utf-8").read()
    farben = {k: (int(v, 16) >> 16 & 255, int(v, 16) >> 8 & 255, int(v, 16) & 255)
              for k, v in re.findall(r'\["(\w+)"\]\s*=\s*0x([0-9A-Fa-f]{6})', text)}
    defs = dict(re.findall(r'\["([\w@]+)"\]\s*=\s*"([^"]*)"', text))
    return farben, defs


FARBEN, DEFS = lade_definitionen()


def definition(name, groesse):
    for _ in range(4):
        d = DEFS.get(name)
        if d is None or not d.startswith("="):
            break
        name = d[1:]
    if groesse <= 16 and name + "@16" in DEFS:
        return DEFS[name + "@16"]
    d = DEFS.get(name)
    return None if d is None or d.startswith("=") else d


def dunkel(c):
    return (int(c[0] * 0.45 + 26 * 0.55), int(c[1] * 0.45 + 30 * 0.55), int(c[2] * 0.45 + 36 * 0.55))


def floor(v):
    return math.floor(v + 1e-6)


# ---------------------------------------------------------------- Geometrie (Gerätepixel)

def bogen_punkte(x, y, w, h, start, winkel, n=None):
    """Punkte eines Ellipsenbogens (Winkel im Uhrzeigersinn ab 3 Uhr, wie GDI+)."""
    n = n or max(8, int(abs(winkel) / 4))
    cx, cy, rx, ry = x + w / 2, y + h / 2, w / 2, h / 2
    return [(cx + rx * math.cos(math.radians(start + winkel * i / n)),
             cy + ry * math.sin(math.radians(start + winkel * i / n))) for i in range(n + 1)]


class Teil:
    def __init__(self, punkte, farbe, flaeche, umriss, breite=0.0, geschlossen=True, loch=None):
        self.punkte, self.farbe, self.flaeche, self.umriss = punkte, farbe, flaeche, umriss
        self.breite, self.geschlossen, self.loch = breite, geschlossen, loch


class Zeichner:
    def __init__(self, groesse):
        self.s = groesse / 32.0
        self.rand = max(1, floor(self.s + 0.5))

    def F(self, v):
        return floor(v * self.s + 0.5)

    def Lb(self, b):
        return max(1, floor(b * self.s + 0.5))

    def L(self, v, lb):
        return floor(v * self.s) + 0.5 if int(lb) % 2 == 1 else floor(v * self.s + 0.5)

    def box(self, x, y, w, h):
        x0, y0 = self.F(x), self.F(y)
        return x0, y0, max(1, self.F(x + w) - x0), max(1, self.F(y + h) - y0)

    def ellipse(self, x, y, w, h):
        x0, y0, b, hh = self.box(x, y, w, h)
        return bogen_punkte(x0, y0, b, hh, 0, 360, 96)[:-1]

    def rechteck(self, x, y, w, h, r):
        x0, y0, b, hh = self.box(x, y, w, h)
        d = min(2 * r * self.s, min(b, hh))
        if d < 0.5:
            return [(x0, y0), (x0 + b, y0), (x0 + b, y0 + hh), (x0, y0 + hh)]
        return (bogen_punkte(x0, y0, d, d, 180, 90, 12) + bogen_punkte(x0 + b - d, y0, d, d, 270, 90, 12)
                + bogen_punkte(x0 + b - d, y0 + hh - d, d, d, 0, 90, 12) + bogen_punkte(x0, y0 + hh - d, d, d, 90, 90, 12))

    @staticmethod
    def kopf(lb):
        return max(lb * 2.2, 3.5), max(lb * 1.6, 2.5)

    def spitze(self, p, d, lb, farbe, saum, an_spitze):
        ln = math.hypot(*d)
        u = (1.0, 0.0) if ln < 1e-4 else (d[0] / ln, d[1] / ln)
        kl, kb = self.kopf(lb)
        basis = (p[0] - u[0] * kl, p[1] - u[1] * kl) if an_spitze else p
        sp = p if an_spitze else (p[0] + u[0] * kl, p[1] + u[1] * kl)
        n = (-u[1], u[0])
        return Teil([sp, (basis[0] + n[0] * kb, basis[1] + n[1] * kb), (basis[0] - n[0] * kb, basis[1] - n[1] * kb)],
                    farbe, True, saum), basis

    def befehl(self, befehl):
        tok = befehl.split()
        z, farbe, umriss, saum, breite = [], (255, 0, 255), True, False, 2.0
        for t in tok[1:]:
            try:
                z.append(float(t))
                continue
            except ValueError:
                pass
            if t == "!":
                umriss = False
            elif t == "h":
                saum = True
            elif len(t) > 1 and t[0] == "w" and (t[1].isdigit() or t[1] == "."):
                breite = float(t[1:])
            else:
                if t.endswith("!"):   # „hb!“ = Farbe ohne Umriss
                    umriss = False
                farbe = FARBEN.get(t.rstrip("!"), (255, 0, 255))
        lb = self.Lb(breite)
        op, s = tok[0], self.s
        if op == "R":
            return [Teil(self.rechteck(z[0], z[1], z[2], z[3], z[4] if len(z) > 4 else 0), farbe, True, umriss)]
        if op == "E":
            return [Teil(self.ellipse(*z[:4]), farbe, True, umriss)]
        if op == "C":
            x, y, w, h = self.box(*z[:4])
            return [Teil([(x + w / 2, y + h / 2)] + bogen_punkte(x, y, w, h, z[4], z[5]), farbe, True, umriss)]
        if op == "H":
            x, y, g = z[0], z[1], z[2]
            bx, by, bw, bh = self.box(x, y + 0.48 * g, g, 1.04 * g)
            return [Teil(self.ellipse(x + 0.29 * g, y, 0.42 * g, 0.42 * g), farbe, True, umriss),
                    Teil([(bx + bw / 2, by + bh / 2)] + bogen_punkte(bx, by, bw, bh, 180, 180), farbe, True, umriss)]
        if op == "P":
            return [Teil([(self.F(z[i]), self.F(z[i + 1])) for i in range(0, len(z) - 1, 2)], farbe, True, umriss)]
        if op == "S":
            n = int(z[4])
            pts = []
            for i in range(2 * n):
                a = -math.pi / 2 + i * math.pi / n
                r = z[2] if i % 2 == 0 else z[3]
                pts.append(((z[0] + r * math.cos(a)) * s, (z[1] + r * math.sin(a)) * s))
            return [Teil(pts, farbe, True, umriss)]
        if op == "Z":
            cx, cy, r, n = z[0], z[1], z[2], int(z[3])
            ri, schritt, pts = r * 0.74, 2 * math.pi / n, []
            for i in range(n):
                a = i * schritt - math.pi / 2
                for rr, da in ((ri, -0.27), (r, -0.15), (r, 0.15), (ri, 0.27)):
                    pts.append(((cx + rr * math.cos(a + da * schritt)) * s, (cy + rr * math.sin(a + da * schritt)) * s))
            return [Teil(pts, farbe, True, umriss, loch=(cx * s, cy * s, r * 0.34 * s))]
        if op == "L":
            pts = [(self.L(z[i], lb), self.L(z[i + 1], lb)) for i in range(0, len(z) - 1, 2)]
            return [Teil(pts, farbe, False, saum, lb, False)]
        if op in ("A", "B"):
            x, y = self.L(z[0], lb), self.L(z[1], lb)
            w, h = self.L(z[0] + z[2], lb) - x, self.L(z[1] + z[3], lb) - y
            teile = [Teil(bogen_punkte(x, y, w, h, z[4], z[5]), farbe, False, saum, lb, False)]
            if op == "B":
                e = math.radians(z[4] + z[5])
                ende = (x + w / 2 + w / 2 * math.cos(e), y + h / 2 + h / 2 * math.sin(e))
                d = (-w / 2 * math.sin(e), h / 2 * math.cos(e))
                if z[5] < 0:
                    d = (-d[0], -d[1])
                teile.append(self.spitze(ende, d, lb, farbe, saum, False)[0])
            return teile
        if op == "V":
            pts = [(self.L(z[i], lb), self.L(z[i + 1], lb)) for i in range(0, len(z) - 1, 2)]
            sp, vor = pts[-1], pts[-2]
            teil, basis = self.spitze(sp, (sp[0] - vor[0], sp[1] - vor[1]), lb, farbe, saum, True)
            pts[-1] = basis
            return [Teil(pts, farbe, False, saum, lb, False), teil]
        return []


# ---------------------------------------------------------------- Rasterung (Pillow, überabgetastet)

def _strich(d, pts, breite, farbe, k, geschlossen):
    p = [(x * k, y * k) for x, y in pts]
    if geschlossen:
        p = p + [p[0]]
    w = max(1, int(round(breite * k)))
    if len(p) > 1:
        d.line(p, fill=farbe, width=w, joint="curve")
    r = breite * k / 2
    for x, y in (p if geschlossen or len(p) < 40 else [p[0], p[-1]]):
        d.ellipse([x - r, y - r, x + r, y + r], fill=farbe)


def render(defn, groesse, k=None):
    k = k or (8 if groesse <= 64 else 4)
    n = groesse * k
    bild = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    z = Zeichner(groesse)
    for gruppe in [g.strip() for g in defn.split(";") if g.strip()]:
        teile = []
        for b in [b.strip() for b in gruppe.split("&") if b.strip()]:
            teile += z.befehl(b)
        for durchgang in ("umriss", "fuellung"):
            for t in teile:
                if durchgang == "umriss" and not t.umriss:
                    continue
                ebene = Image.new("RGBA", (n, n), (0, 0, 0, 0))
                d = ImageDraw.Draw(ebene)
                if durchgang == "umriss":
                    c = dunkel(t.farbe) + (255,)
                    if t.flaeche:
                        d.polygon([(x * k, y * k) for x, y in t.punkte], fill=c)
                        _strich(d, t.punkte, 2 * z.rand, c, k, True)
                    else:
                        _strich(d, t.punkte, t.breite + 2 * z.rand, c, k, False)
                    if t.loch:
                        cx, cy, r = t.loch
                        r = (r - z.rand) * k
                        d.ellipse([cx * k - r, cy * k - r, cx * k + r, cy * k + r], fill=(0, 0, 0, 0))
                else:
                    c = tuple(t.farbe) + (255,)
                    if t.flaeche:
                        d.polygon([(x * k, y * k) for x, y in t.punkte], fill=c)
                        if t.loch:
                            cx, cy, r = t.loch
                            d.ellipse([(cx - r) * k, (cy - r) * k, (cx + r) * k, (cy + r) * k], fill=(0, 0, 0, 0))
                    else:
                        _strich(d, t.punkte, t.breite, c, k, False)
                bild = Image.alpha_composite(bild, ebene)
    return bild.resize((groesse, groesse), Image.BOX)


def symbol(name, groesse):
    d = definition(name, groesse)
    if d is None:
        d = "R 3 3 26 26 6 hg"
    return render(d, groesse)


# ---------------------------------------------------------------- SVG-Master

def svg(defn, einheit=32, groesse=256):
    """Vektorfassung (ohne Pixelrundung) im 32er-Raster."""
    z = Zeichner(32)   # Maßstab 1: Gerätekoordinaten = Rasterkoordinaten
    z.F = lambda v: v
    z.L = lambda v, lb: v
    z.Lb = lambda b: b
    teile_svg = []
    for gruppe in [g.strip() for g in defn.split(";") if g.strip()]:
        teile = []
        for b in [b.strip() for b in gruppe.split("&") if b.strip()]:
            teile += z.befehl(b)
        def pfad(t):
            pts = " ".join(f"{x:.2f},{y:.2f}" for x, y in t.punkte)
            return ("M" + pts + (" Z" if t.geschlossen else ""))
        for t in teile:
            if t.umriss:
                c = "#%02x%02x%02x" % dunkel(t.farbe)
                w = 2 if t.flaeche else t.breite + 2
                fill = c if t.flaeche else "none"
                teile_svg.append(f'<path d="{pfad(t)}" fill="{fill}" stroke="{c}" stroke-width="{w:g}" stroke-linejoin="round" stroke-linecap="round"/>')
        for t in teile:
            c = "#%02x%02x%02x" % tuple(t.farbe)
            if t.flaeche:
                teile_svg.append(f'<path d="{pfad(t)}" fill="{c}"/>')
            else:
                teile_svg.append(f'<path d="{pfad(t)}" fill="none" stroke="{c}" stroke-width="{t.breite:g}" stroke-linejoin="round" stroke-linecap="round"/>')
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{groesse}" height="{groesse}" viewBox="0 0 {einheit} {einheit}">\n'
            '  <title>UntPlan</title>\n  ' + "\n  ".join(teile_svg) + "\n</svg>\n")


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


# ---------------------------------------------------------------- Vorschaubogen

def vorschau(pfad):
    namen = sorted(k for k in DEFS if "@" not in k)
    hintergruende = [(255, 255, 255), (243, 243, 243), (0x12, 0x5E, 0x7A), (0x2E, 0x70, 0x40)]
    spalten = 4
    zelle_b, zelle_h = 330, 46
    zeilen = (len(namen) + spalten - 1) // spalten
    bogen = Image.new("RGBA", (spalten * zelle_b + 20, zeilen * zelle_h + 60), (250, 250, 250, 255))
    d = ImageDraw.Draw(bogen)
    try:
        schrift = ImageFont.truetype("DejaVuSans.ttf", 12)
        titel = ImageFont.truetype("DejaVuSans-Bold.ttf", 16)
    except OSError:
        schrift = titel = ImageFont.load_default()
    d.text((10, 10), "UntPlan – Symbole (16 px und 32 px auf Weiß, Hellgrau, Petrol, Grün)", fill=(30, 30, 30), font=titel)
    for i, name in enumerate(namen):
        x0 = 10 + (i % spalten) * zelle_b
        y0 = 40 + (i // spalten) * zelle_h
        x = x0
        for hg in hintergruende:
            d.rectangle([x, y0, x + 56, y0 + 40], fill=hg + (255,))
            bogen.alpha_composite(symbol(name, 16), (x + 4, y0 + 12))
            bogen.alpha_composite(symbol(name, 32), (x + 22, y0 + 4))
            x += 58
        d.text((x + 4, y0 + 14), name, fill=(30, 30, 30), font=schrift)
    gross = bogen.convert("RGB")
    gross.save(pfad)
    # Vergrößerte Ansicht (Faktor 3, ohne Glättung) zur Pixelkontrolle
    basis, ext = os.path.splitext(pfad)
    gross.resize((gross.width * 2, gross.height * 2), Image.NEAREST).save(basis + "-2x" + ext)


def main(argv):
    logo = DEFS["logo"]
    groessen = [16, 24, 32, 48, 64, 256]
    bilder = [symbol("logo", g) for g in groessen]
    with open(os.path.join(HIER, "untplan.ico"), "wb") as f:
        f.write(ico(bilder))
    bilder[-1].save(os.path.join(HIER, "untplan-logo-256.png"), optimize=True)
    bilder[-1].save(os.path.join(HIER, "icon.png"), optimize=True)
    with open(os.path.join(HIER, "untplan-logo.svg"), "w", encoding="utf-8") as f:
        f.write(svg(logo))
    if "--vorschau" in argv:
        vorschau(argv[argv.index("--vorschau") + 1])
    print("Logo und Symbole erzeugt.")


if __name__ == "__main__":
    main(sys.argv[1:])
