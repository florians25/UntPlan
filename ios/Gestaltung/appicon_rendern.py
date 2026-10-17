"""App-Symbol von UntPlanMobile aus SVG rendern (hell, dunkel, getönt; je 1024 px, Xcode erzeugt die übrigen Größen).

Aufruf (im Ordner ios/):
    python3 Gestaltung/appicon_rendern.py UntPlanMobile/Assets.xcassets/AppIcon.appiconset /pfad/vorschau.png
Benötigt cairosvg und Pillow. Die SVG-Quellen landen neben der Vorschau.
Geometrie wie das Web-Logo (web/.../img/logo.svg): Kalender mit Ringen, Kopflinie und Stundenblöcken, flach, ohne Verläufe.
"""
import cairosvg, sys, os
from PIL import Image
out = sys.argv[1]; vorschau = sys.argv[2]
S = 28.0          # Skalierung Logo-Raster (28er) -> 1024
OX = 512 - 14*S
OY = 512 - 13.9*S

def glyph(linie, block, akzent):
    # Geometrie wie web/.../img/logo.svg, kräftiger und mit gefüllten Blöcken
    return f'''
  <g transform="translate({OX:.2f} {OY:.2f}) scale({S})" fill="none" stroke="{linie}" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round">
    <rect x="3.25" y="5.25" width="21.5" height="19.5" rx="3.4"/>
    <path d="M9 2.9v4.4M19 2.9v4.4M3.25 10.6h21.5"/>
    <g stroke="none">
      <rect x="6.6" y="13.4" width="3.8" height="3.1" rx=".8" fill="{block}"/>
      <rect x="6.6" y="18.4" width="3.8" height="3.0" rx=".8" fill="{block}"/>
      <rect x="12.1" y="13.4" width="3.8" height="8.0" rx=".8" fill="{akzent}"/>
      <rect x="17.6" y="13.4" width="3.8" height="4.6" rx=".8" fill="{block}"/>
    </g>
  </g>'''

def svg(hinter, inhalt):
    bg = f'<rect width="1024" height="1024" fill="{hinter}"/>' if hinter else ''
    return f'<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024" viewBox="0 0 1024 1024">{bg}{inhalt}</svg>'

varianten = {
    # hell: ruhiges Orange (Akzent F56B2A), Symbol weiß, mittlerer Block warm-hell
    'AppIcon.png':        svg('#F56B2A', glyph('#FFFFFF', '#FFFFFF', '#FFE3D3')),
    # dunkel (ab iOS 18): transparenter Grund, Symbol im Akzent
    'AppIcon-Dark.png':   svg(None, glyph('#F56B2A', '#F56B2A', '#FFB48C')),
    # getönt (ab iOS 18): Graustufen auf transparentem Grund
    'AppIcon-Tinted.png': svg(None, glyph('#FFFFFF', '#FFFFFF', '#BDBDBD')),
}
os.makedirs(out, exist_ok=True)
for name, s in varianten.items():
    open(os.path.join(os.path.dirname(vorschau), name.replace('.png', '.svg')), 'w').write(s)
    p = os.path.join(out, name)
    cairosvg.svg2png(bytestring=s.encode(), write_to=p, output_width=1024, output_height=1024)
    im = Image.open(p)
    im = im.convert('RGB') if name == 'AppIcon.png' else im.convert('LA' if 'Tinted' in name else 'RGBA')
    im.save(p, optimize=True)

# Vorschau: hell, dunkel (auf iOS-Dunkelgrund), getönt (orange getönt) mit Maske
def maske(n):
    m = Image.new('L', (n*4, n*4), 0)
    from PIL import ImageDraw
    ImageDraw.Draw(m).rounded_rectangle([0,0,n*4-1,n*4-1], radius=int(n*4*0.2237), fill=255)
    return m.resize((n,n), Image.LANCZOS)
n = 300; pad = 40
V = Image.new('RGB', (pad + 3*(n+pad), n + 2*pad + 0), (238,240,243))
hell = Image.open(os.path.join(out,'AppIcon.png')).convert('RGBA').resize((n,n), Image.LANCZOS)
dg = Image.new('RGBA', (n,n), (32,32,34,255)); dg.alpha_composite(Image.open(os.path.join(out,'AppIcon-Dark.png')).convert('RGBA').resize((n,n), Image.LANCZOS))
t = Image.open(os.path.join(out,'AppIcon-Tinted.png')).convert('LA').resize((n,n), Image.LANCZOS)
tg = Image.new('RGBA', (n,n), (20,20,22,255))
lum, a = t.split()
farbe = Image.new('RGBA', (n,n), (245,170,120,255)); farbe.putalpha(Image.eval(a, lambda v: v).point(lambda v: v))
from PIL import ImageChops
farbe.putalpha(ImageChops.multiply(a, lum))
tg.alpha_composite(farbe)
for i, im in enumerate([hell, dg, tg]):
    V.paste(im, (pad + i*(n+pad), pad), maske(n))
V.save(vorschau)
