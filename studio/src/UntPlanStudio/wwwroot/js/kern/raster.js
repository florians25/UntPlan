// Stundenplan-Raster: Woche (Tage × Stunden), zusammenhängende Stunden als Block (Doppel-/Blockstunden),
// Kopplungen nebeneinander, gesperrte Zeiten schraffiert. Drag & Drop: Stunden verschieben bzw. nicht verplante Stunden setzen
// (Zielfelder werden nach dem Zielstatus des Servers eingefärbt: grün frei, rot Vorgabe verletzt, violett kein Raum, Rahmen = Tausch).
import { h, leeren } from './dom.js';

export const TYP_STUNDE = 'application/x-untplan-stunde';
export const TYP_UNTERRICHT = 'application/x-untplan-unterricht';

export class Stundenplanraster {
  /**
   * @param {HTMLElement} eltern
   * @param {object} o
   * @param {'klasse'|'lehrer'|'raum'|'fach'} [o.typ]
   * @param {boolean} [o.kompakt]
   * @param {boolean} [o.ziehen] – Drag & Drop erlaubt
   * @param {(eintrag, tag, stunde) => void} [o.beiKlick]
   * @param {(eintrag, tag, stunde) => void} [o.beiDoppelklick]
   * @param {(daten:{u, tag?, stunde?}) => Promise<string[][]|null>} [o.beiZiehenStart] – liefert Zielstatus [tag][stunde-1]
   * @param {(daten:{u, tag?, stunde?, zielTag, zielStunde}) => void} [o.beiAblegen]
   * @param {(eintrag, tag, stunde, ev) => void} [o.kontextmenue]
   */
  constructor(eltern, o = {}) {
    this.o = o;
    this.el = h('div', { class: `raster${o.kompakt ? ' kompakt' : ''}` });
    this.gitter = h('div', { class: 'r-gitter' });
    this.el.append(this.gitter);
    eltern.append(this.el);
    this.felder = new Map();
    this.gewaehlt = null;
  }

  /** Plan setzen: Antwort von GET /stundenplan/{typ}/{id} ({ element, zeitraster, zellen }). */
  setzen(plan) {
    this.plan = plan;
    if (plan?.element?.typ) this.o.typ = plan.element.typ;
    this._zeichnen();
  }

  _zeichnen() {
    leeren(this.gitter);
    this.felder.clear();
    const p = this.plan;
    if (!p) return;
    const { tage, stunden, belegung } = p.zeitraster;
    this.gitter.style.setProperty('--tage', tage.length);
    this.gitter.style.gridTemplateRows = `26px repeat(${stunden.length}, minmax(${this.o.kompakt ? 30 : 46}px, 1fr))`;
    this.gitter.append(h('div', { class: 'r-ecke', style: { gridRow: '1', gridColumn: '1' } }));
    tage.forEach((t, i) => this.gitter.append(h('div', { class: 'r-kopf', style: { gridRow: '1', gridColumn: String(i + 2) }, title: t.lang }, t.kurz)));
    stunden.forEach((s, j) => {
      const pause = s.pauseDanach ? ' pause-danach' : '';
      this.gitter.append(h('div', { class: `r-stunde${pause}`, style: { gridRow: String(j + 2), gridColumn: '1' }, title: s.pauseDanach ? `danach Pause: ${s.pauseDanach}` : '' },
        h('span', { class: 'nr' }, s.label), s.beginn ? h('span', { class: 'zeit' }, `${s.beginn}–${s.ende}`) : null));
      tage.forEach((t, i) => {
        const art = belegung?.[i]?.[j] ?? 'V';
        const feld = h('div', { class: `r-feld${pause}${art === '' ? ' gesperrt' : art === 'N' ? ' nachmittag' : ''}`, style: { gridRow: String(j + 2), gridColumn: String(i + 2) } });
        feld.dataset.tag = t.nr;
        feld.dataset.stunde = s.nr;
        this._ablageEinrichten(feld);
        this.felder.set(`${t.nr}:${s.nr}`, feld);
        this.gitter.append(feld);
      });
    });

    // Einträge je Feld, dann gleiche Folgestunden zu Blöcken zusammenfassen
    const zellen = new Map(p.zellen.map((z) => [`${z.tag}:${z.stunde}`, z.eintraege]));
    const schluessel = (l) => (l || []).map((e) => `${e.u}.${e.zeile}`).sort().join('|');
    tage.forEach((t, i) => {
      let j = 0;
      while (j < stunden.length) {
        const eintraege = zellen.get(`${t.nr}:${stunden[j].nr}`);
        if (!eintraege?.length) { j++; continue; }
        const k = schluessel(eintraege);
        let n = 1;
        while (j + n < stunden.length && schluessel(zellen.get(`${t.nr}:${stunden[j + n].nr}`)) === k) n++;
        this.gitter.append(this._block(eintraege, t.nr, stunden[j].nr, i, j, n));
        j += n;
      }
    });
  }

  _block(eintraege, tag, stunde, spalte, zeile, laenge) {
    const block = h('div', { class: 'r-block', style: { gridColumn: String(spalte + 2), gridRow: `${zeile + 2} / span ${laenge}` } });
    for (const e of eintraege) block.append(this._eintrag(e, tag, stunde));
    return block;
  }

  _eintrag(e, tag, stunde) {
    const typ = this.o.typ;
    const zeilen = typ === 'lehrer' ? [e.klassen, e.raum] : typ === 'raum' ? [e.klassen, e.lehrer] : typ === 'fach' ? [e.klassen, e.lehrer] : [e.lehrer, e.raum];
    const el = h('div', {
      class: `r-eintrag${e.gekoppelt ? ' gekoppelt' : ''}`,
      title: `U-Nr. ${e.u}${e.gekoppelt ? ' (gekoppelt)' : ''}\n${e.fach} – ${e.klassen} – ${e.lehrer}${e.raum ? ` – ${e.raum}` : ''}${e.text ? `\n${e.text}` : ''}${e.fix ? '\nfixiert' : ''}`,
      draggable: this.o.ziehen && !e.fix ? 'true' : null,
    },
    h('span', { class: 'fach' }, e.fach || '—'),
    h('span', { class: 'zeile2' }, zeilen[0] || ''),
    this.o.kompakt ? null : h('span', { class: 'zeile3' }, zeilen[1] || ''),
    e.fix ? h('span', { class: 'fix' }, '*') : null);
    if (e.farbe) el.style.background = `color-mix(in srgb, ${e.farbe} 55%, white)`;
    if (e.schrift) el.style.color = e.schrift;
    el.addEventListener('click', () => { this._waehlen(el); this.o.beiKlick?.(e, tag, stunde); });
    el.addEventListener('dblclick', () => this.o.beiDoppelklick?.(e, tag, stunde));
    el.addEventListener('contextmenu', (ev) => { if (this.o.kontextmenue) { ev.preventDefault(); this._waehlen(el); this.o.kontextmenue(e, tag, stunde, ev); } });
    el.addEventListener('dragstart', (ev) => {
      ev.dataTransfer.setData(TYP_STUNDE, JSON.stringify({ u: e.u, tag, stunde }));
      ev.dataTransfer.effectAllowed = 'move';
      this.ziehenBeginnt({ u: e.u, tag, stunde });
    });
    el.addEventListener('dragend', () => this.ziehenEndet());
    return el;
  }

  _waehlen(el) {
    this.gewaehlt?.classList.remove('gewaehlt');
    this.gewaehlt = el;
    el.classList.add('gewaehlt');
  }

  /** Beginn eines Ziehvorgangs (auch von außen, z. B. aus der Liste nicht verplanter Stunden). */
  async ziehenBeginnt(daten) {
    setTimeout(() => this.el.classList.add('zieht')); // nicht synchron im dragstart (sonst bricht Chromium das Ziehen ab)
    if (this.o.beiZiehenStart) {
      try { this.ziele(await this.o.beiZiehenStart(daten)); } catch { /* ohne Einfärbung */ }
    }
  }

  ziehenEndet() {
    this.el.classList.remove('zieht');
    this.ziele(null);
  }

  /** Zielfelder einfärben (Matrix [tag][stunde-1] mit Frei|VorgabeVerletzt|KeinRaum|TauschMoeglich|Kollision) oder zurücksetzen. */
  ziele(matrix) {
    for (const [k, feld] of this.felder) {
      feld.classList.remove('ziel-Frei', 'ziel-VorgabeVerletzt', 'ziel-KeinRaum', 'ziel-TauschMoeglich', 'drueber');
      if (!matrix) continue;
      const [t, s] = k.split(':').map(Number);
      const st = matrix[t]?.[s - 1];
      if (st && st !== 'Kollision') feld.classList.add(`ziel-${st}`);
    }
  }

  _ablageEinrichten(feld) {
    const passt = (e) => e.dataTransfer.types.includes(TYP_STUNDE) || e.dataTransfer.types.includes(TYP_UNTERRICHT);
    feld.addEventListener('dragover', (e) => { if (this.o.ziehen && passt(e)) { e.preventDefault(); feld.classList.add('drueber'); } });
    feld.addEventListener('dragleave', () => feld.classList.remove('drueber'));
    feld.addEventListener('drop', (e) => {
      e.preventDefault();
      feld.classList.remove('drueber');
      const roh = e.dataTransfer.getData(TYP_STUNDE) || e.dataTransfer.getData(TYP_UNTERRICHT);
      this.ziehenEndet();
      if (!roh) return;
      const d = JSON.parse(roh);
      this.o.beiAblegen?.({ ...d, zielTag: Number(feld.dataset.tag), zielStunde: Number(feld.dataset.stunde) });
    });
  }
}
