// Stundenplan-Raster: Woche (Tage × Stunden), zusammenhängende Stunden als Block (Doppel-/Blockstunden),
// Kopplungen nebeneinander, gesperrte Zeiten schraffiert. Drag & Drop: Stunden verschieben bzw. nicht verplante Stunden setzen
// (Zielfelder werden nach dem Zielstatus des Servers eingefärbt: grün frei, rot Vorgabe verletzt, violett kein Raum, Rahmen = Tausch;
// liefert der Server Gründe je Feld, erscheinen sie beim Ziehen am Mauszeiger – Kollisionsanzeige wie im Desktop).
// Mehrfachauswahl (Option mehrfach): Strg/Umschalt+Klick markiert weitere Stunden; ein Block wird an der Stelle gegriffen, an der
// man ihn anfasst (Umschalt beim Ziehen = nur diese eine Stunde des Blocks).
import { h, leeren } from './dom.js';

export const TYP_STUNDE = 'application/x-untplan-stunde';
export const TYP_UNTERRICHT = 'application/x-untplan-unterricht';

const schluesselVon = (u, tag, stunde) => `${u}:${tag}:${stunde}`;

export class Stundenplanraster {
  /**
   * @param {HTMLElement} eltern
   * @param {object} o
   * @param {'klasse'|'lehrer'|'raum'|'fach'} [o.typ]
   * @param {boolean} [o.kompakt]
   * @param {boolean} [o.ziehen] – Drag & Drop erlaubt
   * @param {boolean} [o.mehrfach] – Mehrfachauswahl mit Strg/Umschalt+Klick
   * @param {(eintrag, tag, stunde) => void} [o.beiKlick]
   * @param {(eintrag, tag, stunde) => void} [o.beiDoppelklick]
   * @param {(daten:{u, tag?, stunde?, einzeln?, laenge?}) => Promise<string[][]|{status:string[][], gruende?:string[][]}|null>} [o.beiZiehenStart]
   *        – liefert Zielstatus [tag][stunde-1] (optional mit Gründen)
   * @param {(daten:{u, tag?, stunde?, einzeln?, laenge?, zielTag, zielStunde}) => void} [o.beiAblegen]
   * @param {(eintrag, tag, stunde, ev) => void} [o.kontextmenue]
   * @param {(tag, stunde, ev) => void} [o.feldKontextmenue] – Rechtsklick auf ein leeres Feld
   * @param {(markierte:Array) => void} [o.beiMarkierung]
   */
  constructor(eltern, o = {}) {
    this.o = o;
    this.el = h('div', { class: `raster${o.kompakt ? ' kompakt' : ''}`, tabindex: '0' });
    this.gitter = h('div', { class: 'r-gitter' });
    this.grund = h('div', { class: 'r-grund', hidden: true });
    this.el.append(this.gitter);
    eltern.append(this.el);
    document.body.append(this.grund);
    this.felder = new Map();
    this.gewaehlt = null;
    this._gewaehltKey = null;
    this.markiert = new Map();
    this._gruende = null;
    this.el.addEventListener('dragover', (e) => this._grundZeigen(e));
    this.el.addEventListener('dragleave', (e) => { if (!this.el.contains(e.relatedTarget)) this.grund.hidden = true; });
  }

  /** Plan setzen: Antwort von GET /stundenplan/{typ}/{id} ({ element, zeitraster, zellen }). */
  setzen(plan) {
    this.plan = plan;
    if (plan?.element?.typ) this.o.typ = plan.element.typ;
    this._zeichnen();
  }

  /** Aufräumen (z. B. beim Schließen des Fensters). */
  entfernen() { this.grund.remove(); }

  _zeichnen() {
    leeren(this.gitter);
    this.felder.clear();
    this.gewaehlt = null;
    const alteMarkierung = new Set(this.markiert.keys());
    this.markiert.clear();
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
        feld.addEventListener('contextmenu', (ev) => { if (this.o.feldKontextmenue) { ev.preventDefault(); this.o.feldKontextmenue(t.nr, s.nr, ev); } });
        feld.addEventListener('click', () => { if (!this.markiert.size) return; this._markierungLeeren(); this.o.beiMarkierung?.(this.markierte()); });
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
        this.gitter.append(this._block(eintraege, t.nr, stunden[j].nr, i, j, n, stunden.slice(j, j + n).map((s) => s.nr)));
        j += n;
      }
    });
    // Auswahl und Markierung nach dem Neuzeichnen wiederherstellen
    for (const el of this.gitter.querySelectorAll('.r-eintrag')) {
      const d = el._daten;
      if (!d) continue;
      if (d.stunden.some((s) => schluesselVon(d.e.u, d.tag, s) === this._gewaehltKey)) this._waehlen(el, this._gewaehltKey);
      if (d.stunden.some((s) => alteMarkierung.has(schluesselVon(d.e.u, d.tag, s)))) this._markieren(el, true);
    }
  }

  _block(eintraege, tag, stunde, spalte, zeile, laenge, stundenListe) {
    const block = h('div', { class: 'r-block', style: { gridColumn: String(spalte + 2), gridRow: `${zeile + 2} / span ${laenge}` } });
    for (const e of eintraege) block.append(this._eintrag(e, tag, stunde, stundenListe));
    return block;
  }

  _eintrag(e, tag, stunde, stundenListe = [stunde]) {
    const typ = this.o.typ;
    const zeilen = typ === 'lehrer' ? [e.klassen, e.raum] : typ === 'raum' ? [e.klassen, e.lehrer] : typ === 'fach' ? [e.klassen, e.lehrer] : [e.lehrer, e.raum];
    const el = h('div', {
      class: `r-eintrag${e.gekoppelt ? ' gekoppelt' : ''}`,
      title: `U-Nr. ${e.u}${e.gekoppelt ? ' (gekoppelt)' : ''}${e.woche ? ` · Woche ${e.woche}` : ''}\n${e.fach} – ${e.klassen} – ${e.lehrer}${e.raum ? ` – ${e.raum}` : ''}${e.text ? `\n${e.text}` : ''}${e.fix ? '\nfixiert' : ''}`,
      draggable: this.o.ziehen && !e.fix ? 'true' : null,
    },
    h('span', { class: 'fach' }, e.fach || '—'),
    h('span', { class: 'zeile2' }, zeilen[0] || ''),
    this.o.kompakt ? null : h('span', { class: 'zeile3' }, zeilen[1] || ''),
    e.fix ? h('span', { class: 'fix' }, '*') : null,
    e.woche ? h('span', { class: 'woche', title: `Woche ${e.woche}` }, e.woche) : null);
    el._daten = { e, tag, stunde, stunden: stundenListe, laenge: stundenListe.length };
    if (e.farbe) el.style.background = `color-mix(in srgb, ${e.farbe} 55%, white)`;
    if (e.schrift) el.style.color = e.schrift;
    el.addEventListener('click', (ev) => {
      ev.stopPropagation();
      if (this.o.mehrfach && (ev.ctrlKey || ev.metaKey || ev.shiftKey)) {
        if (this.gewaehlt && !this.markiert.size) this._markieren(this.gewaehlt, true);
        this._markieren(el, !el.classList.contains('markiert'));
        this.o.beiMarkierung?.(this.markierte());
        return;
      }
      if (this.markiert.size) { this._markierungLeeren(); this.o.beiMarkierung?.(this.markierte()); }
      this._waehlen(el);
      this.el.focus({ preventScroll: true });
      this.o.beiKlick?.(e, tag, stunde);
    });
    el.addEventListener('dblclick', () => this.o.beiDoppelklick?.(e, tag, stunde));
    el.addEventListener('contextmenu', (ev) => {
      if (!this.o.kontextmenue) return;
      ev.preventDefault();
      ev.stopPropagation();
      if (!el.classList.contains('markiert')) {
        if (this.markiert.size) this._markierungLeeren();
        this._waehlen(el);
      }
      this.o.kontextmenue(e, tag, stunde, ev);
    });
    el.addEventListener('dragstart', (ev) => {
      // gegriffene Stunde im Block aus der Mausposition; Umschalt = nur diese Stunde
      const r = el.getBoundingClientRect();
      const idx = Math.max(0, Math.min(stundenListe.length - 1, Math.floor((ev.clientY - r.top) / (r.height / stundenListe.length))));
      const daten = { u: e.u, tag, stunde: stundenListe[idx], einzeln: !!ev.shiftKey && stundenListe.length > 1, laenge: stundenListe.length };
      ev.dataTransfer.setData(TYP_STUNDE, JSON.stringify(daten));
      ev.dataTransfer.effectAllowed = 'move';
      this.ziehenBeginnt(daten);
    });
    el.addEventListener('dragend', () => this.ziehenEndet());
    return el;
  }

  _waehlen(el, key) {
    this.gewaehlt?.classList.remove('gewaehlt');
    this.gewaehlt = el;
    el.classList.add('gewaehlt');
    const d = el._daten;
    this._gewaehltKey = key || (d ? schluesselVon(d.e.u, d.tag, d.stunde) : null);
  }

  _markieren(el, an) {
    const d = el._daten;
    if (!d) return;
    el.classList.toggle('markiert', an);
    for (const s of d.stunden) {
      const k = schluesselVon(d.e.u, d.tag, s);
      if (an) this.markiert.set(k, { u: d.e.u, tag: d.tag, stunde: s, e: d.e, el });
      else this.markiert.delete(k);
    }
  }

  _markierungLeeren() {
    for (const el of this.gitter.querySelectorAll('.r-eintrag.markiert')) el.classList.remove('markiert');
    this.markiert.clear();
  }

  /** Markierung aufheben (von außen). */
  markierungAufheben() { this._markierungLeeren(); this.o.beiMarkierung?.([]); }

  /** Gewählte Stunde { u, tag, stunde, laenge, stunden, e } oder null. */
  auswahl() {
    const d = this.gewaehlt?._daten;
    return d ? { u: d.e.u, tag: d.tag, stunde: d.stunde, laenge: d.laenge, stunden: d.stunden, e: d.e } : null;
  }

  /** Markierte Stunden (je Stunde ein Eintrag { u, tag, stunde, e }); ohne Markierung alle Stunden des gewählten Blocks. */
  markierte() {
    if (this.markiert.size) {
      const gesehen = new Set();
      return [...this.markiert.values()].filter((m) => { const k = schluesselVon(m.u, m.tag, m.stunde); if (gesehen.has(k)) return false; gesehen.add(k); return true; });
    }
    const d = this.gewaehlt?._daten;
    return d ? d.stunden.map((s) => ({ u: d.e.u, tag: d.tag, stunde: s, e: d.e })) : [];
  }

  /** Alle Stunden markieren, die filter(eintrag) erfüllen (ohne Filter: alle). */
  alleMarkieren(filter) {
    this._markierungLeeren();
    for (const el of this.gitter.querySelectorAll('.r-eintrag')) if (!filter || filter(el._daten.e)) this._markieren(el, true);
    this.o.beiMarkierung?.(this.markierte());
  }

  /** Beginn eines Ziehvorgangs (auch von außen, z. B. aus der Liste nicht verplanter Stunden). */
  async ziehenBeginnt(daten) {
    setTimeout(() => this.el.classList.add('zieht')); // nicht synchron im dragstart (sonst bricht Chromium das Ziehen ab)
    this._gruende = null;
    if (this.o.beiZiehenStart) {
      try {
        const antwort = await this.o.beiZiehenStart(daten);
        if (antwort && !Array.isArray(antwort)) { this._gruende = antwort.gruende || null; this.ziele(antwort.status); } else this.ziele(antwort);
      } catch { /* ohne Einfärbung */ }
    }
  }

  ziehenEndet() {
    this.el.classList.remove('zieht');
    this.grund.hidden = true;
    this._gruende = null;
    this.ziele(null);
  }

  /** Zielfelder einfärben (Matrix [tag][stunde-1] mit Frei|VorgabeVerletzt|KeinRaum|TauschMoeglich|Kollision) oder zurücksetzen. */
  ziele(matrix) {
    for (const [k, feld] of this.felder) {
      feld.classList.remove('ziel-Frei', 'ziel-VorgabeVerletzt', 'ziel-KeinRaum', 'ziel-TauschMoeglich', 'ziel-Kollision', 'drueber');
      if (!matrix) continue;
      const [t, s] = k.split(':').map(Number);
      const st = matrix[t]?.[s - 1];
      if (st) feld.classList.add(`ziel-${st}`);
    }
  }

  /** Grund des Zielfeldes unter dem Mauszeiger anzeigen (Kollision, verletzte Vorgabe, Tauschpartner). */
  _grundZeigen(e) {
    if (!this._gruende) { this.grund.hidden = true; return; }
    const feld = e.target.closest?.('.r-feld') || this._feldBei(e.clientX, e.clientY);
    const text = feld ? this._gruende[Number(feld.dataset.tag)]?.[Number(feld.dataset.stunde) - 1] : '';
    if (!text) { this.grund.hidden = true; return; }
    const st = [...feld.classList].find((k) => k.startsWith('ziel-'))?.slice(5) || '';
    this.grund.className = `r-grund ${st}`;
    this.grund.textContent = text;
    this.grund.hidden = false;
    this.grund.style.left = `${Math.min(e.clientX + 14, innerWidth - 300)}px`;
    this.grund.style.top = `${e.clientY + 16}px`;
  }

  _feldBei(x, y) {
    for (const feld of this.felder.values()) {
      const r = feld.getBoundingClientRect();
      if (x >= r.left && x < r.right && y >= r.top && y < r.bottom) return feld;
    }
    return null;
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
