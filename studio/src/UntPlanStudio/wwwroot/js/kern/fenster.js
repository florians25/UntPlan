// Fenster-Manager: Unterfenster im Arbeitsbereich öffnen, verschieben, Größe ändern, minimieren, maximieren, andocken
// (an Rand/Ecke ziehen), anordnen (überlappend, nebeneinander, untereinander) und als Fensteranordnung speichern.
//
// Fenstertypen registrieren die Module (siehe modulsystem.js):
//   { titel, symbol, symbolFuer(parameter), breite, hoehe, einzig(parameter) → Schlüssel|null, erneutGeoeffnet(fenster, parameter), erstellen(fenster, parameter) }
import { h, leeren, entprellen } from './dom.js';
import { symbol } from './symbole.js';
import { an, melden } from './ereignisse.js';
import { fehlerMelden } from './dialog.js';

const typen = new Map();
const fenster = [];
let flaeche = null, leiste = null, vorschau = null;
let zNaechster = 10, zaehler = 0;
let aenderungMelden = () => {};

export function fenstertypRegistrieren(name, def) {
  typen.set(name, def);
}

export function fenstertyp(name) { return typen.get(name); }

export class Fenster {
  constructor(typName, parameter, lage = {}) {
    this.id = `f${++zaehler}`;
    this.typ = typName;
    this.def = typen.get(typName);
    this.parameter = parameter ?? {};
    this.andock = lage.andock || null;
    this.symbolName = this.def?.symbolFuer?.(this.parameter) || this.def?.symbol || 'fenster';
    this._abmelden = [];
    this._schliessenRueckrufe = [];

    this.titelText = h('span', { class: 'text' }, this.def?.titel || typName);
    this.knoepfe = {
      min: h('button', { type: 'button', title: 'Minimieren', onclick: (e) => { e.stopPropagation(); this.minimieren(); } }, symbol('minimieren')),
      max: h('button', { type: 'button', title: 'Maximieren', onclick: (e) => { e.stopPropagation(); this.maximieren(); } }, symbol('maximieren')),
      zu: h('button', { type: 'button', class: 'zu', title: 'Schließen', onclick: (e) => { e.stopPropagation(); this.schliessen(); } }, symbol('schliessen')),
    };
    this.titelleiste = h('div', { class: 'fenster-titel' }, h('span', { class: 'symbol' }, symbol(this.symbolName)), this.titelText,
      this.knoepfe.min, this.knoepfe.max, this.knoepfe.zu);
    this.werkzeuge = h('div', { class: 'fenster-werkzeuge' });
    this.inhalt = h('div', { class: 'fenster-inhalt' });
    this.fuss = h('div', { class: 'fenster-fuss' });
    this.el = h('section', { class: 'fenster', 'aria-label': this.def?.titel || typName }, this.titelleiste, this.werkzeuge, this.inhalt, this.fuss,
      ...['n', 's', 'w', 'o', 'nw', 'no', 'sw', 'so'].map((r) => h('div', { class: `griff ${r}`, dataset: { richtung: r } })));
    this.el.dataset.typ = typName;

    const r = flaeche.getBoundingClientRect();
    const b = Math.min(lage.b ?? this.def?.breite ?? 760, r.width - 20), hh = Math.min(lage.h ?? this.def?.hoehe ?? 440, r.height - 20);
    const versatz = (fenster.length % 8) * 26;
    this.lage = { x: lage.x ?? 12 + versatz, y: lage.y ?? 10 + versatz, b, h: hh };
    this._anwenden();

    this.el.addEventListener('pointerdown', () => this.aktivieren(), true);
    this.titelleiste.addEventListener('pointerdown', (e) => this._ziehen(e));
    this.titelleiste.addEventListener('dblclick', (e) => { if (!e.target.closest('button')) this.maximieren(); });
    for (const g of this.el.querySelectorAll('.griff')) g.addEventListener('pointerdown', (e) => this._groesse(e, g.dataset.richtung));
  }

  // ---- für die Fensterinhalte ----

  /** Titel setzen. */
  titel(text) { this.titelText.textContent = text; this.el.setAttribute('aria-label', text); leisteZeichnen(); }

  /** Parameter ändern (z. B. gewähltes Element) – wird in der Fensteranordnung gespeichert. */
  parameterSetzen(p) { this.parameter = { ...this.parameter, ...p }; aenderungMelden(); }

  /** Knopf in der Werkzeugleiste. */
  werkzeug({ text, symbol: sym, titel, aktion, nurSymbol = false }) {
    const k = h('button', { class: 'wz-knopf', type: 'button', title: titel || text },
      sym ? symbol(sym) : null, nurSymbol ? null : h('span', null, text));
    k.addEventListener('click', () => Promise.resolve().then(aktion).catch(fehlerMelden));
    this.werkzeuge.append(k);
    return k;
  }

  trenner() { this.werkzeuge.append(h('span', { class: 'trenner' })); }

  /** Ereignis abonnieren, das beim Schließen automatisch abgemeldet wird. */
  an(name, f) { this._abmelden.push(an(name, f)); }

  beimSchliessen(f) { this._schliessenRueckrufe.push(f); }

  // ---- Fensterfunktionen ----

  aktivieren() {
    if (fensterverwaltung.aktiv() === this && !this.el.classList.contains('minimiert')) return;
    for (const f of fenster) f.el.classList.toggle('aktiv', f === this);
    this.el.classList.remove('minimiert');
    this.el.style.zIndex = String(++zNaechster);
    leisteZeichnen();
    melden('fenster-aktiv', this);
  }

  minimieren() {
    this.el.classList.add('minimiert');
    this.el.classList.remove('aktiv');
    const naechstes = fenster.filter((f) => f !== this && !f.el.classList.contains('minimiert')).sort((a, b) => b.el.style.zIndex - a.el.style.zIndex)[0];
    naechstes?.aktivieren();
    leisteZeichnen();
    aenderungMelden();
  }

  maximieren() {
    this.andock = this.andock === 'max' ? null : 'max';
    this._anwenden();
    aenderungMelden();
  }

  schliessen() {
    const i = fenster.indexOf(this);
    if (i < 0) return;
    fenster.splice(i, 1);
    for (const f of this._schliessenRueckrufe) { try { f(); } catch (e) { console.error(e); } }
    for (const ab of this._abmelden) ab();
    this.el.remove();
    const naechstes = [...fenster].sort((a, b) => b.el.style.zIndex - a.el.style.zIndex)[0];
    naechstes?.aktivieren();
    leisteZeichnen();
    melden('fenster-geschlossen', this);
    aenderungMelden();
  }

  _anwenden() {
    const W = flaeche.clientWidth, H = flaeche.clientHeight;
    const a = this.andock;
    let { x, y, b, h: hh } = this.lage;
    if (a) {
      const halbB = Math.round(W / 2), halbH = Math.round(H / 2);
      const ziele = {
        max: { x: 0, y: 0, b: W, h: H }, links: { x: 0, y: 0, b: halbB, h: H }, rechts: { x: halbB, y: 0, b: W - halbB, h: H },
        lo: { x: 0, y: 0, b: halbB, h: halbH }, ro: { x: halbB, y: 0, b: W - halbB, h: halbH },
        lu: { x: 0, y: halbH, b: halbB, h: H - halbH }, ru: { x: halbB, y: halbH, b: W - halbB, h: H - halbH },
      };
      ({ x, y, b, h: hh } = ziele[a] || ziele.max);
    } else {
      // im sichtbaren Bereich halten
      x = Math.max(-b + 80, Math.min(x, W - 80));
      y = Math.max(0, Math.min(y, H - 28));
    }
    Object.assign(this.el.style, { left: `${x}px`, top: `${y}px`, width: `${b}px`, height: `${hh}px` });
    this.el.classList.toggle('maximiert', a === 'max');
    this.knoepfe.max.replaceChildren(symbol(a === 'max' ? 'wiederherstellen' : 'maximieren'));
    this.knoepfe.max.title = a === 'max' ? 'Wiederherstellen' : 'Maximieren';
  }

  _ziehen(e) {
    if (e.button !== 0 || e.target.closest('button')) return;
    e.preventDefault();
    const start = { x: e.clientX, y: e.clientY };
    const r0 = flaeche.getBoundingClientRect();
    let lage = { ...this.lage };
    let bewegt = false, ziel = null;
    if (this.andock) {
      // aus angedocktem/maximiertem Zustand lösen: Fenster unter dem Mauszeiger behalten
      const anteil = (e.clientX - this.el.getBoundingClientRect().left) / this.el.offsetWidth;
      lage.x = e.clientX - r0.left - lage.b * anteil;
      lage.y = 0;
    }
    const x0 = lage.x, y0 = lage.y;
    const bewegen = (ev) => {
      const dx = ev.clientX - start.x, dy = ev.clientY - start.y;
      if (!bewegt && Math.abs(dx) + Math.abs(dy) < 4) return;
      if (!bewegt) { bewegt = true; this.andock = null; this.el.classList.add('zieht'); }
      this.lage = { ...lage, x: x0 + dx, y: y0 + dy };
      this._anwenden();
      ziel = andockZiel(ev.clientX - r0.left, ev.clientY - r0.top, r0.width, r0.height);
      vorschauZeigen(ziel);
    };
    const ende = () => {
      removeEventListener('pointermove', bewegen);
      removeEventListener('pointerup', ende);
      this.el.classList.remove('zieht');
      vorschauZeigen(null);
      if (bewegt) {
        if (ziel) { this.andock = ziel; this._anwenden(); }
        aenderungMelden();
      }
    };
    addEventListener('pointermove', bewegen);
    addEventListener('pointerup', ende);
  }

  _groesse(e, richtung) {
    e.preventDefault();
    e.stopPropagation();
    if (this.andock) { this.lage = this._istLage(); this.andock = null; }
    const start = { x: e.clientX, y: e.clientY }, l0 = { ...this.lage };
    const bewegen = (ev) => {
      const dx = ev.clientX - start.x, dy = ev.clientY - start.y;
      const l = { ...l0 };
      if (richtung.includes('o')) l.b = Math.max(220, l0.b + dx);
      if (richtung.includes('s')) l.h = Math.max(120, l0.h + dy);
      if (richtung.includes('w')) { l.b = Math.max(220, l0.b - dx); l.x = l0.x + l0.b - l.b; }
      if (richtung.startsWith('n')) { l.h = Math.max(120, l0.h - dy); l.y = Math.max(0, l0.y + l0.h - l.h); }
      this.lage = l;
      this._anwenden();
    };
    const ende = () => {
      removeEventListener('pointermove', bewegen);
      removeEventListener('pointerup', ende);
      aenderungMelden();
    };
    addEventListener('pointermove', bewegen);
    addEventListener('pointerup', ende);
  }

  _istLage() {
    return { x: this.el.offsetLeft, y: this.el.offsetTop, b: this.el.offsetWidth, h: this.el.offsetHeight };
  }

  /** Beschreibung für Fensteranordnungen. */
  beschreibung() {
    return { typ: this.typ, parameter: this.parameter, ...this.lage, andock: this.andock, minimiert: this.el.classList.contains('minimiert') };
  }
}

function andockZiel(x, y, W, H) {
  const rand = 14;
  const links = x < rand, rechts = x > W - rand, oben = y < rand, unten = y > H - rand;
  if (oben && links) return 'lo';
  if (oben && rechts) return 'ro';
  if (unten && links) return 'lu';
  if (unten && rechts) return 'ru';
  if (oben) return 'max';
  if (links) return 'links';
  if (rechts) return 'rechts';
  return null;
}

function vorschauZeigen(ziel) {
  if (!ziel) { vorschau.hidden = true; return; }
  const W = flaeche.clientWidth, H = flaeche.clientHeight, hb = W / 2, hh = H / 2;
  const r = { max: [0, 0, W, H], links: [0, 0, hb, H], rechts: [hb, 0, hb, H], lo: [0, 0, hb, hh], ro: [hb, 0, hb, hh], lu: [0, hh, hb, hh], ru: [hb, hh, hb, hh] }[ziel];
  Object.assign(vorschau.style, { left: `${r[0] + 4}px`, top: `${r[1] + 4}px`, width: `${r[2] - 8}px`, height: `${r[3] - 8}px` });
  vorschau.hidden = false;
}

function leisteZeichnen() {
  if (!leiste) return;
  leeren(leiste);
  const aktiv = fensterverwaltung.aktiv();
  for (const f of fenster) {
    const min = f.el.classList.contains('minimiert');
    leiste.append(h('button', {
      type: 'button', class: `${f === aktiv && !min ? 'aktiv' : ''}${min ? ' minimiert' : ''}`, title: f.titelText.textContent,
      onclick: () => (f === aktiv && !min ? f.minimieren() : f.aktivieren()),
    }, symbol(f.symbolName), h('span', null, f.titelText.textContent)));
  }
}

export const fensterverwaltung = {
  /** Arbeitsbereich einrichten (einmalig durch die Shell). */
  einrichten(flaecheEl, leisteEl, beiAenderung) {
    flaeche = flaecheEl;
    leiste = leisteEl;
    vorschau = h('div', { class: 'andock-vorschau', hidden: true });
    flaeche.append(vorschau);
    aenderungMelden = entprellen(() => beiAenderung?.(), 500);
    new ResizeObserver(() => { for (const f of fenster) f._anwenden(); }).observe(flaeche);
  },

  /**
   * Fenster öffnen. Ist der Typ „einzig“ (gleicher Schlüssel), wird das vorhandene Fenster aktiviert.
   * lage: { x, y, b, h, andock, minimiert }
   */
  oeffnen(typ, parameter = {}, lage = {}) {
    const def = typen.get(typ);
    if (!def) { fehlerMelden(new Error(`Unbekannter Fenstertyp „${typ}“.`)); return null; }
    const key = def.einzig?.(parameter);
    if (key) {
      const vorhanden = fenster.find((f) => f.typ === typ && f.def.einzig?.(f.parameter) === key);
      if (vorhanden) { vorhanden.aktivieren(); vorhanden.def.erneutGeoeffnet?.(vorhanden, parameter); return vorhanden; }
    }
    const f = new Fenster(typ, parameter, lage);
    fenster.push(f);
    flaeche.append(f.el);
    f.aktivieren();
    try {
      const erg = def.erstellen(f, f.parameter);
      if (erg?.catch) erg.catch((e) => { fehlerMelden(e); f.inhalt.append(h('div', { class: 'fehlertext', style: { padding: '12px' } }, String(e.message || e))); });
    } catch (e) {
      fehlerMelden(e);
      f.inhalt.append(h('div', { class: 'fehlertext', style: { padding: '12px' } }, String(e.message || e)));
    }
    if (lage.minimiert) f.minimieren();
    leisteZeichnen();
    aenderungMelden();
    return f;
  },

  alle: () => [...fenster],
  aktiv: () => fenster.find((f) => f.el.classList.contains('aktiv')) || null,

  /** Anordnen: 'ueberlappend' | 'nebeneinander' | 'untereinander'. */
  anordnen(art) {
    const sichtbar = fenster.filter((f) => !f.el.classList.contains('minimiert'));
    const W = flaeche.clientWidth, H = flaeche.clientHeight, n = sichtbar.length;
    if (!n) return;
    sichtbar.forEach((f, i) => {
      f.andock = null;
      if (art === 'ueberlappend') f.lage = { x: 12 + i * 26, y: 10 + i * 26, b: Math.min(820, W - 40 - i * 26), h: Math.min(520, H - 30 - i * 26) };
      else if (art === 'nebeneinander') {
        const b = Math.floor(W / n);
        f.lage = { x: i * b, y: 0, b: i === n - 1 ? W - i * b : b, h: H };
      } else {
        const hh = Math.floor(H / n);
        f.lage = { x: 0, y: i * hh, b: W, h: i === n - 1 ? H - i * hh : hh };
      }
      f._anwenden();
    });
    aenderungMelden();
  },

  alleSchliessen() {
    for (const f of [...fenster]) f.schliessen();
  },

  /** Aktuelle Anordnung (serialisierbar). */
  anordnung: () => [...fenster].sort((a, b) => a.el.style.zIndex - b.el.style.zIndex).map((f) => f.beschreibung()),

  /** Anordnung wiederherstellen (vorhandene Fenster werden geschlossen). */
  anordnungLaden(liste) {
    this.alleSchliessen();
    for (const b of liste || []) {
      if (!typen.has(b.typ)) continue;
      this.oeffnen(b.typ, b.parameter, b);
    }
  },
};
