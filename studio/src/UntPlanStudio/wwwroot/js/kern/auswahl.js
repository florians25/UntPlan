// Vorschlagsliste für Eingabefelder: Elemente mit Kurz- und Langname (wie die Auswahllisten im Desktop).
// Für Listen (z. B. Klassen „5a,5b“) wird der Teil nach dem letzten Komma ergänzt.
import { h, leeren } from './dom.js';

export class Vorschlagsliste {
  /**
   * @param {HTMLInputElement} input
   * @param {{ optionen: () => Promise<Array<{k:string,l:string}>>|Array, liste?: boolean, beiWahl?: (wert:string) => void }} o
   */
  constructor(input, o) {
    this.input = input;
    this.o = o;
    this.el = null;
    this.fokus = -1;
    this.treffer = [];
    this._eingabe = () => this.zeigen();
    this._blur = () => setTimeout(() => this.schliessen(), 120);
    input.addEventListener('input', this._eingabe);
    input.addEventListener('blur', this._blur);
    input.setAttribute('autocomplete', 'off');
  }

  get offen() { return !!this.el; }

  token() {
    const v = this.input.value;
    if (!this.o.liste) return v.trim();
    const i = Math.max(v.lastIndexOf(','), v.lastIndexOf(';'));
    return v.slice(i + 1).trim();
  }

  async zeigen(alle = false) {
    const t = alle ? '' : this.token().toLowerCase();
    const liste = await this.o.optionen();
    // inzwischen geschlossen (Eingabe beendet, bevor die Liste geladen war): nicht mehr anzeigen
    if (this._entfernt || !this.input.isConnected) return;
    const a = [], b = [], c = [];
    for (const e of liste) {
      const k = e.k.toLowerCase();
      if (!t || k.startsWith(t)) a.push(e);
      else if (k.includes(t)) b.push(e);
      else if ((e.l || '').toLowerCase().includes(t)) c.push(e);
    }
    this.treffer = [...a, ...b, ...c].slice(0, 300);
    if (!this.el) {
      this.el = h('div', { class: 'vorschlaege', role: 'listbox' });
      this.el.addEventListener('pointerdown', (e) => e.preventDefault());
      document.body.append(this.el);
    }
    leeren(this.el);
    if (this.treffer.length === 0) this.el.append(h('div', { class: 'v-leer' }, 'Kein passender Eintrag'));
    this.treffer.forEach((e, i) => {
      this.el.append(h('div', { class: `v-eintrag${e.c ? ` v-${e.c}` : ''}`, role: 'option', onclick: () => this.waehlen(i) }, h('span', { class: 'k' }, e.k), h('span', { class: 'l' }, e.l || '')));
    });
    this.fokus = t && this.treffer.length ? 0 : -1;
    this.markieren();
    const r = this.input.getBoundingClientRect();
    this.el.style.minWidth = `${Math.max(180, r.width)}px`;
    const unten = innerHeight - r.bottom;
    this.el.style.left = `${Math.min(r.left, innerWidth - 260)}px`;
    if (unten < 200 && r.top > unten) { this.el.style.top = ''; this.el.style.bottom = `${innerHeight - r.top}px`; }
    else { this.el.style.bottom = ''; this.el.style.top = `${r.bottom}px`; }
  }

  markieren() {
    if (!this.el) return;
    [...this.el.querySelectorAll('.v-eintrag')].forEach((x, i) => x.classList.toggle('fokus', i === this.fokus));
    this.el.querySelector('.v-eintrag.fokus')?.scrollIntoView({ block: 'nearest' });
  }

  waehlen(i) {
    const e = this.treffer[i];
    if (!e) return;
    if (this.o.liste) {
      const v = this.input.value;
      const p = Math.max(v.lastIndexOf(','), v.lastIndexOf(';'));
      this.input.value = (p >= 0 ? v.slice(0, p + 1) : '') + e.k;
    } else this.input.value = e.k;
    this.schliessen();
    this.o.beiWahl?.(this.input.value);
  }

  /** Taste im Eingabefeld behandeln; true = verbraucht. */
  taste(e) {
    if ((e.key === 'ArrowDown' && e.altKey) || e.key === 'F4') { e.preventDefault(); this.zeigen(true); return true; }
    if (!this.el) return false;
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      const n = this.treffer.length;
      if (n) this.fokus = (this.fokus + (e.key === 'ArrowDown' ? 1 : -1) + n) % n;
      this.markieren();
      return true;
    }
    if ((e.key === 'Enter' || e.key === 'Tab') && this.fokus >= 0) {
      const t = this.token().toLowerCase();
      // nur übernehmen, wenn der Eintrag nicht bereits exakt eingegeben ist
      if (this.treffer[this.fokus].k.toLowerCase() !== t) { e.preventDefault(); this.waehlen(this.fokus); return e.key === 'Enter'; }
      this.schliessen();
      return false;
    }
    if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); this.schliessen(); return true; }
    return false;
  }

  schliessen() {
    this.el?.remove();
    this.el = null;
    this.fokus = -1;
  }

  entfernen() {
    this._entfernt = true;
    this.schliessen();
    this.input.removeEventListener('input', this._eingabe);
    this.input.removeEventListener('blur', this._blur);
  }
}
