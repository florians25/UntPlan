// UntPlanWeb – Kalender: eigener Datumswähler im Portal-Design für alle Datumsangaben der Oberfläche.
// Monatsansicht mit Spalte „KW“, heute markiert, Ferien und Feiertage dezent hinterlegt, Monats- und Jahresübersicht,
// vollständige Tastaturbedienung, Wochenauswahl (Stundenplan) und Bereichsauswahl (Von/Bis-Paare).
//
// Datumsfelder (<input type="date"> und "datetime-local") werden beim Einfügen in die Seite automatisch zu Textfeldern mit
// diesem Kalender (beobachten()). Für den übrigen Code ändert sich nichts: .value liefert und nimmt weiterhin ISO-Daten
// (JJJJ-MM-TT bzw. JJJJ-MM-TTThh:mm), input/change werden wie bisher ausgelöst, required, min und max gelten weiter.
// Angezeigt wird TT.MM.JJJJ; Eintippen (auch „17.10.“, „17.10.26“ oder „2026-10-17“) ist möglich.
import { heuteIso, plusTage, montag, ausIso, iso, WOCHENTAGE } from './ui.js';

const MONATE = ['Januar', 'Februar', 'März', 'April', 'Mai', 'Juni', 'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember'];
const MONATE_KURZ = ['Jan', 'Feb', 'Mär', 'Apr', 'Mai', 'Jun', 'Jul', 'Aug', 'Sep', 'Okt', 'Nov', 'Dez'];
const WT = ['Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa', 'So'];
const ISO_TAG = /^\d{4}-\d{2}-\d{2}$/;
const FREI_TEXT = { ferien: 'Ferien', feiertag: 'Feiertag', frei: 'Unterrichtsfrei' };

const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const gueltig = s => ISO_TAG.test(s) && iso(ausIso(s)) === s;
const wochentag = s => (ausIso(s).getDay() + 6) % 7;
const datumDe = s => s ? `${s.slice(8, 10)}.${s.slice(5, 7)}.${s.slice(0, 4)}` : '';
const langText = s => { const d = ausIso(s); return `${WOCHENTAGE[wochentag(s)]}, ${d.getDate()}. ${MONATE[d.getMonth()]} ${d.getFullYear()}`; };
const monatsErster = (j, m) => iso(new Date(j, m, 1));
const reduziert = () => { try { return matchMedia('(prefers-reduced-motion: reduce)').matches; } catch { return false; } };
const grobZeiger = () => { try { return matchMedia('(pointer: coarse)').matches; } catch { return false; } };

/** Kalenderwoche nach ISO 8601. */
export function kalenderwoche(isoDatum) {
  const d = new Date(isoDatum + 'T12:00:00Z');
  const tag = (d.getUTCDay() + 6) % 7;
  d.setUTCDate(d.getUTCDate() - tag + 3);
  const ersterDo = new Date(Date.UTC(d.getUTCFullYear(), 0, 4));
  return 1 + Math.round(((d - ersterDo) / 864e5 - 3 + ((ersterDo.getUTCDay() + 6) % 7)) / 7);
}

// ------------------------------------------------------------------ Ferien, Feiertage, freie Tage

let freiLaden = null;
let freie = [];
function freieTageLaden() {
  if (!freiLaden) {
    freiLaden = fetch('/api/app/kalender', { headers: { 'X-UntPlanWeb': '1' }, credentials: 'same-origin' })
      .then(r => (r.ok ? r.json() : null))
      .then(d => { if (!d) freiLaden = null; freie = d?.freieTage ?? []; return freie; })
      .catch(() => { freiLaden = null; return freie; });
  }
  return freiLaden;
}
window.addEventListener('untplan:abgemeldet', () => { freiLaden = null; freie = []; });

/** Freier Tag (Ferien vor Feiertag vor unterrichtsfrei) an einem Datum oder null. */
export function freierTag(isoDatum) {
  let treffer = null;
  for (const f of freie) {
    if (isoDatum < f.von || isoDatum > f.bis) continue;
    if (!treffer || f.art === 'feiertag' || (f.art === 'ferien' && treffer.art === 'frei')) treffer = f;
  }
  return treffer;
}

// ------------------------------------------------------------------ Kalender-Popover

let offen = null;

/**
 * Kalender an einem Element öffnen. Optionen:
 *  modus 'tag' | 'woche' | 'bereich'; wert (Tag bzw. ein Tag der Woche); von/bis/phase ('von' | 'bis') für Bereiche;
 *  min, max (ISO); zeit ('hh:mm' – zusätzlich Uhrzeit wählen); monate (1 | 2); mitte (unter dem Element zentriert);
 *  beiWahl(iso | {von, bis}, zeit); beiLeeren(); leeren (Knopf „Leeren“); fokusSetzen; fokusZurueck; beimSchliessen().
 */
export function kalenderOeffnen(anker, optionen = {}) {
  offen?.schliessen(false, true);
  offen = new Kalender(anker, optionen);
  return offen;
}

/** Offener Kalender (oder null) – z. B. für Tests und Tastaturbedienung. */
export const offenerKalender = () => offen;

class Kalender {
  constructor(anker, o) {
    this.anker = anker;
    this.o = o;
    this.modus = o.modus ?? 'tag';
    this.min = gueltig(o.min ?? '') ? o.min : '';
    this.max = gueltig(o.max ?? '') ? o.max : '';
    this.wert = gueltig(o.wert ?? '') ? o.wert : '';
    this.von = gueltig(o.von ?? '') ? o.von : '';
    this.bis = gueltig(o.bis ?? '') ? o.bis : '';
    this.bisVorher = this.bis;
    this.phase = o.phase === 'bis' && this.von ? 'bis' : 'von';
    this.geaendert = false;
    this.zeit = o.zeit ?? null;
    const start = this.modus === 'bereich' ? (this.phase === 'bis' ? this.bis || this.von : this.von) : this.wert;
    this.fokus = this.klemmen(start || heuteIso());
    this.ansicht = 'tage';
    this.monate = o.monate ?? (this.modus === 'bereich' && innerWidth >= 720 ? 2 : 1);
    this.zeigen(this.fokus);
    this.bauen();
  }

  klemmen(d) { if (this.min && d < this.min) return this.min; if (this.max && d > this.max) return this.max; return d; }
  erlaubt(d) { return (!this.min || d >= this.min) && (!this.max || d <= this.max); }
  /** Ersten angezeigten Monat so setzen, dass d sichtbar ist. */
  zeigen(d) {
    const x = ausIso(d);
    const erster = this.j === undefined ? null : new Date(this.j, this.m, 1);
    if (erster) {
      const diff = (x.getFullYear() - erster.getFullYear()) * 12 + x.getMonth() - erster.getMonth();
      if (diff >= 0 && diff < this.monate) return;
      if (diff === this.monate) { this.schritt(1); return; }
    }
    this.j = x.getFullYear(); this.m = x.getMonth();
  }
  schritt(n) { const d = new Date(this.j, this.m + n, 1); this.j = d.getFullYear(); this.m = d.getMonth(); }

  bauen() {
    const pop = document.createElement('div');
    pop.className = `kal-pop kal-m-${this.modus}${this.monate > 1 ? ' kal-zwei' : ''}`;
    pop.setAttribute('role', 'dialog');
    pop.setAttribute('aria-label', this.modus === 'woche' ? 'Woche wählen' : this.modus === 'bereich' ? 'Zeitraum wählen' : 'Datum wählen');
    this.pop = pop;
    const ziel = this.anker.closest('.modal') ?? document.body;
    this.blatt = innerWidth < 576;
    if (this.blatt) {
      this.schleier = document.createElement('div');
      this.schleier.className = 'kal-schleier';
      this.schleier.addEventListener('click', () => this.schliessen(true));
      ziel.append(this.schleier);
      pop.classList.add('kal-blatt');
    }
    ziel.append(pop);
    this.anker.setAttribute('aria-expanded', 'true');
    this.zeichnen();
    this.platzieren();
    if (!reduziert()) pop.classList.add('kal-ein');

    pop.addEventListener('click', ev => this.klick(ev));
    pop.addEventListener('keydown', ev => this.taste(ev));
    pop.addEventListener('mouseover', ev => this.ueber(ev));
    pop.addEventListener('focusin', ev => this.ueber(ev));
    pop.addEventListener('mouseleave', () => this.infoSetzen(null));
    this.aussen = ev => { if (!this.anker.isConnected) { this.schliessen(false); return; } if (!pop.contains(ev.target) && !this.anker.contains(ev.target) && !this.schleier?.contains(ev.target)) this.schliessen(true); };
    this.fokusRaus = ev => {
      const nach = ev.relatedTarget;
      if (nach && !pop.contains(nach) && nach !== this.anker) this.schliessen(true);
    };
    this.lage = ev => { if (!this.anker.isConnected || ev?.type === 'hashchange') { this.schliessen(false); return; } this.platzieren(); };
    document.addEventListener('pointerdown', this.aussen, true);
    pop.addEventListener('focusout', this.fokusRaus);
    window.addEventListener('resize', this.lage);
    window.addEventListener('scroll', this.lage, true);
    window.addEventListener('hashchange', this.lage);
    if (this.o.fokusSetzen ?? this.anker.tagName !== 'INPUT') this.fokusieren();
    freieTageLaden().then(() => { if (offen === this && freie.length) { const f = this.pop.contains(document.activeElement); this.zeichnen(); if (f) this.fokusieren(); } });
  }

  platzieren() {
    const p = this.pop;
    if (this.blatt) return;
    const r = this.anker.getBoundingClientRect();
    const b = innerWidth, h = innerHeight;
    const w = p.offsetWidth, ph = p.offsetHeight;
    let links = this.o.mitte ? r.left + r.width / 2 - w / 2 : r.left;
    links = Math.max(8, Math.min(links, b - w - 8));
    let oben = r.bottom + 8;
    if (oben + ph > h - 8 && r.top - 8 - ph >= 8) { oben = r.top - 8 - ph; p.classList.add('kal-oben'); }
    else { oben = Math.max(8, Math.min(oben, h - ph - 8)); p.classList.remove('kal-oben'); }
    p.style.left = `${Math.round(links)}px`;
    p.style.top = `${Math.round(oben)}px`;
  }

  // ---------------------------------------------------------------- Darstellung

  zeichnen() {
    const t = this.ansicht;
    let kopf, rumpf;
    if (t === 'tage') {
      const titel = [];
      for (let i = 0; i < this.monate; i++) { const d = new Date(this.j, this.m + i, 1); titel.push(`${MONATE[d.getMonth()]} ${d.getFullYear()}`); }
      kopf = titel.map(x => `<button type="button" class="kal-titel" data-ansicht="monate" aria-label="${x} – Monat wählen">${x}<i class="bi bi-chevron-down" aria-hidden="true"></i></button>`).join('');
      rumpf = `<div class="kal-monate">${Array.from({ length: this.monate }, (_, i) => { const d = new Date(this.j, this.m + i, 1); return this.monatHtml(d.getFullYear(), d.getMonth()); }).join('')}</div>`;
    } else if (t === 'monate') {
      kopf = `<button type="button" class="kal-titel" data-ansicht="jahre" aria-label="${this.j} – Jahr wählen">${this.j}<i class="bi bi-chevron-down" aria-hidden="true"></i></button>`;
      const jetzt = heuteIso().slice(0, 7);
      rumpf = `<div class="kal-raster">${MONATE_KURZ.map((n, i) => {
        const schl = `${this.j}-${String(i + 1).padStart(2, '0')}`;
        const aus = (this.min && `${schl}-31` < this.min) || (this.max && `${schl}-01` > this.max);
        return `<button type="button" data-monat="${i}" class="${schl === jetzt ? 'k-heute' : ''} ${i === this.m ? 'k-gewaehlt' : ''}" ${aus ? 'disabled' : ''} aria-label="${MONATE[i]} ${this.j}">${n}</button>`;
      }).join('')}</div>`;
    } else {
      const start = this.j - (this.j % 12);
      kopf = `<span class="kal-titel">${start} – ${start + 11}</span>`;
      const jetzt = new Date().getFullYear();
      rumpf = `<div class="kal-raster">${Array.from({ length: 12 }, (_, i) => start + i).map(j => {
        const aus = (this.min && `${j}-12-31` < this.min) || (this.max && `${j}-01-01` > this.max);
        return `<button type="button" data-jahr="${j}" class="${j === jetzt ? 'k-heute' : ''} ${j === this.j ? 'k-gewaehlt' : ''}" ${aus ? 'disabled' : ''}>${j}</button>`;
      }).join('')}</div>`;
    }
    const zurueck = t === 'tage' ? 'Vorheriger Monat' : t === 'monate' ? 'Vorheriges Jahr' : 'Frühere Jahre';
    const vor = t === 'tage' ? 'Nächster Monat' : t === 'monate' ? 'Nächstes Jahr' : 'Spätere Jahre';
    const heute = heuteIso();
    const heuteKnopf = this.modus === 'woche' ? 'Diese Woche' : 'Heute';
    this.pop.innerHTML = `${this.blatt ? '<div class="kal-griff" aria-hidden="true"></div>' : ''}
      <div class="kal-kopf"><button type="button" class="kal-pfeil" data-schritt="-1" aria-label="${zurueck}"><i class="bi bi-chevron-left" aria-hidden="true"></i></button>
        <div class="kal-titelzeile">${kopf}</div>
        <button type="button" class="kal-pfeil" data-schritt="1" aria-label="${vor}"><i class="bi bi-chevron-right" aria-hidden="true"></i></button></div>
      ${rumpf}
      <div class="kal-fuss">
        <span class="kal-info" aria-live="polite" data-info></span>
        ${this.zeit !== null ? `<label class="kal-zeit"><span class="visually-hidden">Uhrzeit</span><input type="time" step="60" value="${esc(this.zeit)}" data-zeit></label>` : ''}
        <span class="kal-aktionen">
          ${this.o.leeren ? '<button type="button" class="kal-leeren" data-leeren>Leeren</button>' : ''}
          <button type="button" class="kal-heute" data-heute ${this.erlaubt(heute) ? '' : 'disabled'}>${heuteKnopf}</button>
          ${this.zeit !== null ? '<button type="button" class="kal-ok" data-ok>Übernehmen</button>' : ''}
        </span>
      </div>`;
    this.infoSetzen(null);
  }

  monatHtml(j, m) {
    const erster = monatsErster(j, m);
    const start = plusTage(erster, -wochentag(erster));
    const heute = heuteIso();
    const [bv, bb] = this.bereich();
    const wochenMo = this.modus === 'woche' && this.wert ? montag(this.wert) : '';
    let zeilen = '';
    for (let w = 0; w < 6; w++) {
      const mo = plusTage(start, w * 7);
      const kw = kalenderwoche(mo);
      let z = `<div class="kal-zeile${mo === wochenMo ? ' k-gewaehlt' : ''}" data-mo="${mo}"><span class="kal-kw" aria-hidden="true">${kw}</span>`;
      for (let t = 0; t < 7; t++) {
        const d = plusTage(mo, t);
        const fremd = Number(d.slice(5, 7)) - 1 !== m;
        const frei = freierTag(d);
        const kl = ['kal-tag'];
        if (fremd) kl.push('k-fremd');
        if (t >= 5) kl.push('k-we');
        if (d === heute) kl.push('k-heute');
        if (frei) kl.push('k-frei', `k-frei-${frei.art}`);
        let gewaehlt = false;
        if (this.modus === 'tag' && d === this.wert) { kl.push('k-gewaehlt'); gewaehlt = true; }
        if (this.modus === 'bereich' && bv) {
          if (d >= bv && d <= bb) kl.push('k-im');
          if (d === bv) { kl.push('k-anfang'); gewaehlt = true; }
          if (d === bb) { kl.push('k-ende'); gewaehlt = true; }
        }
        const label = `${langText(d)}${d === heute ? ', heute' : ''}${frei ? `, ${frei.name || FREI_TEXT[frei.art] || ''}` : ''}`;
        z += `<button type="button" class="${kl.join(' ')}" data-tag="${d}" tabindex="${d === this.fokus ? 0 : -1}" aria-label="${esc(label)}"${gewaehlt ? ' aria-pressed="true"' : ''}${d === heute ? ' aria-current="date"' : ''}${this.erlaubt(d) ? '' : ' disabled'}>${Number(d.slice(8))}</button>`;
      }
      zeilen += z + '</div>';
    }
    return `<div class="kal-monat" role="group" aria-label="${MONATE[m]} ${j}">
      <div class="kal-zeile kal-wt" aria-hidden="true"><span class="kal-kw">KW</span>${WT.map((x, i) => `<span class="${i >= 5 ? 'k-we' : ''}">${x}</span>`).join('')}</div>${zeilen}</div>`;
  }

  /** Aktuell markierter Bereich [von, bis] (bis = von, solange das Ende noch fehlt). */
  bereich() {
    if (this.modus !== 'bereich' || !this.von) return ['', ''];
    if (this.phase === 'bis' && this.geaendert && !this.bis) return [this.von, this.von];
    const b = this.bis && this.bis >= this.von ? this.bis : this.von;
    return [this.von, b];
  }

  infoSetzen(tag) {
    const el = this.pop.querySelector('[data-info]');
    if (!el) return;
    let text = '';
    const f = tag ? freierTag(tag) : null;
    if (f) text = `<i class="kal-punkt k-frei-${esc(f.art)}" aria-hidden="true"></i>${esc(f.name || FREI_TEXT[f.art] || '')}`;
    else if (this.modus === 'bereich') {
      const [v, b] = this.bereich();
      text = this.phase === 'bis' && this.geaendert && !this.bis ? `${datumDe(this.von)} – <span class="kal-hinweis">Ende wählen</span>`
        : v ? `${datumDe(v)} – ${datumDe(b)}` : '<span class="kal-hinweis">Beginn wählen</span>';
    } else if (this.modus === 'woche' && tag) text = `KW ${kalenderwoche(tag)} · ${datumDe(montag(tag)).slice(0, 6)} – ${datumDe(plusTage(montag(tag), 6))}`;
    else if (tag) text = `${WOCHENTAGE[wochentag(tag)]}`;
    else if (this.modus === 'tag' && this.wert) text = `${WOCHENTAGE[wochentag(this.wert)]}, ${datumDe(this.wert)}`;
    else if (this.modus === 'woche' && this.wert) text = `KW ${kalenderwoche(this.wert)}`;
    el.innerHTML = text;
  }

  fokusieren() {
    const b = this.pop.querySelector(this.ansicht === 'tage' ? `[data-tag="${this.fokus}"]` : '.kal-raster .k-gewaehlt, .kal-raster button:not(:disabled)')
      ?? this.pop.querySelector('[data-tag]:not(:disabled), .kal-raster button:not(:disabled)');
    b?.focus({ preventScroll: true });
  }

  // ---------------------------------------------------------------- Bedienung

  ueber(ev) {
    const b = ev.target.closest?.('[data-tag]');
    if (!b) return;
    this.infoSetzen(b.dataset.tag);
    // Bereich: Vorschau bis zum Tag unter dem Zeiger, solange das Ende fehlt
    if (this.modus === 'bereich' && this.phase === 'bis' && this.geaendert && !this.bis) {
      const d = b.dataset.tag;
      const [a, z] = d < this.von ? [d, this.von] : [this.von, d];
      this.pop.querySelectorAll('[data-tag]').forEach(x => x.classList.toggle('k-vorschau', x.dataset.tag >= a && x.dataset.tag <= z));
    }
  }

  klick(ev) {
    const t = ev.target.closest('button');
    if (!t || t.disabled) {
      // Wochenauswahl: Klick auf die KW-Spalte wählt ebenfalls die Woche
      const zeile = ev.target.closest('.kal-zeile[data-mo]');
      if (zeile && this.modus === 'woche') this.waehlen(zeile.dataset.mo);
      return;
    }
    ev.stopPropagation();
    if (t.dataset.schritt) {
      const n = Number(t.dataset.schritt);
      if (this.ansicht === 'tage') this.schritt(n); else if (this.ansicht === 'monate') this.j += n; else this.j += 12 * n;
      this.zeichnen();
      this.pop.querySelector(`[data-schritt="${n}"]`)?.focus({ preventScroll: true });
      return;
    }
    if (t.dataset.ansicht) { this.ansicht = t.dataset.ansicht; this.zeichnen(); this.fokusieren(); this.platzieren(); return; }
    if (t.dataset.monat !== undefined) { this.m = Number(t.dataset.monat); this.ansicht = 'tage'; this.fokusAuf(this.klemmen(monatsErster(this.j, this.m)), true); this.platzieren(); return; }
    if (t.dataset.jahr) { this.j = Number(t.dataset.jahr); this.ansicht = 'monate'; this.zeichnen(); this.fokusieren(); return; }
    if (t.dataset.tag) { this.waehlen(t.dataset.tag); return; }
    if (t.hasAttribute('data-heute')) { const h = heuteIso(); this.ansicht = 'tage'; if (this.modus === 'bereich' || this.zeit !== null) this.fokusAuf(h, true); else this.waehlen(h); return; }
    if (t.hasAttribute('data-leeren')) { const f = this.o.beiLeeren; this.schliessen(false); f?.(); return; }
    if (t.hasAttribute('data-ok')) { this.uebernehmen(); return; }
  }

  /** Fokus (und angezeigten Monat) auf einen Tag setzen. */
  fokusAuf(d, neuZeichnen = false, fokus = true) {
    const vorher = `${this.j}-${this.m}`;
    this.fokus = this.klemmen(d);
    this.zeigen(this.fokus);
    if (neuZeichnen || vorher !== `${this.j}-${this.m}` || !this.pop.querySelector(`[data-tag="${this.fokus}"]`)) this.zeichnen();
    else this.pop.querySelectorAll('[data-tag]').forEach(b => { b.tabIndex = b.dataset.tag === this.fokus ? 0 : -1; });
    if (fokus) this.fokusieren();
    this.infoSetzen(this.fokus);
  }

  waehlen(d) {
    if (!this.erlaubt(d)) return;
    if (this.modus === 'woche') { const f = this.o.beiWahl; this.schliessen(false); f?.(montag(d)); return; }
    if (this.modus === 'bereich') {
      if (this.phase === 'von') { this.von = d; this.bis = ''; this.phase = 'bis'; this.geaendert = true; this.fokusAuf(d, true); return; }
      if (!this.geaendert) {
        // Feld „Bis“ geöffnet: Ende setzen, vor dem Beginn verschiebt sich der Beginn mit
        if (d < this.von) this.von = d;
        this.bis = d;
      } else if (d < this.von) { this.bis = this.von; this.von = d; } else this.bis = d;
      const f = this.o.beiWahl, w = { von: this.von, bis: this.bis };
      this.schliessen(false);
      f?.(w);
      return;
    }
    if (this.zeit !== null) { this.wert = d; this.fokusAuf(d, true); return; }
    const f = this.o.beiWahl;
    this.schliessen(false);
    f?.(d);
  }

  uebernehmen() {
    if (this.zeit !== null) {
      const z = this.pop.querySelector('[data-zeit]')?.value || this.zeit || '00:00';
      const d = this.wert || this.fokus;
      const f = this.o.beiWahl;
      this.schliessen(false);
      f?.(d, z);
    }
  }

  taste(ev) {
    if (ev.key === 'Escape') { ev.preventDefault(); ev.stopPropagation(); this.schliessen(false); return; }
    const tag = ev.target.closest?.('[data-tag]');
    if (ev.key === 'Enter' && ev.target.matches?.('[data-zeit]')) { ev.preventDefault(); this.uebernehmen(); return; }
    if (!tag) return;
    const d = tag.dataset.tag;
    let n = null;
    switch (ev.key) {
      case 'ArrowLeft': n = plusTage(d, -1); break;
      case 'ArrowRight': n = plusTage(d, 1); break;
      case 'ArrowUp': n = plusTage(d, -7); break;
      case 'ArrowDown': n = plusTage(d, 7); break;
      case 'Home': n = plusTage(d, -wochentag(d)); break;
      case 'End': n = plusTage(d, 6 - wochentag(d)); break;
      case 'PageUp': case 'PageDown': {
        const x = ausIso(d), z = ev.key === 'PageUp' ? -1 : 1;
        const ziel = ev.shiftKey ? new Date(x.getFullYear() + z, x.getMonth(), 1) : new Date(x.getFullYear(), x.getMonth() + z, 1);
        const letzter = new Date(ziel.getFullYear(), ziel.getMonth() + 1, 0).getDate();
        n = iso(new Date(ziel.getFullYear(), ziel.getMonth(), Math.min(x.getDate(), letzter)));
        break;
      }
      case 't': case 'T': case 'h': case 'H': n = heuteIso(); break;
      default: return;
    }
    ev.preventDefault();
    ev.stopPropagation();
    // Monat beim Blättern per Bild-Taste immer neu ausrichten (auch bei zwei Monaten)
    if (ev.key.startsWith('Page')) { const x = ausIso(n); this.j = x.getFullYear(); this.m = x.getMonth(); }
    this.fokusAuf(n, ev.key.startsWith('Page'));
  }

  schliessen(uebernehmen = false, ersetzt = false) {
    if (this.zu) return;
    this.zu = true;
    document.removeEventListener('pointerdown', this.aussen, true);
    window.removeEventListener('resize', this.lage);
    window.removeEventListener('scroll', this.lage, true);
    window.removeEventListener('hashchange', this.lage);
    const fokusInnen = this.pop.contains(document.activeElement);
    this.pop.removeEventListener('focusout', this.fokusRaus);
    this.pop.remove();
    this.schleier?.remove();
    this.anker.setAttribute('aria-expanded', 'false');
    if (offen === this) offen = null;
    // Bereich: Beginn gewählt, Ende offen gelassen (Klick daneben) – Beginn übernehmen, Ende nur anpassen, wenn es davor läge
    if (uebernehmen && this.modus === 'bereich' && this.geaendert && !this.bis) {
      this.o.beiWahl?.({ von: this.von, bis: this.bisVorher && this.bisVorher >= this.von ? this.bisVorher : this.von });
    }
    if (fokusInnen && !ersetzt) (this.o.fokusZurueck ?? this.anker)?.focus?.({ preventScroll: true });
    this.o.beimSchliessen?.();
  }
}

/**
 * Kalender an einen Knopf hängen (Stundenplan-Kopf, Tagesansichten); beiWahl(iso).
 * modus 'woche' (gewaehlt = Montag, Klick wählt die ganze Woche) oder 'tag'.
 */
export function kalenderBinden(knopf, gewaehlt, beiWahl, modus = 'woche', weitere = {}) {
  knopf.classList.add('kal-pille');
  knopf.setAttribute('aria-haspopup', 'dialog');
  knopf.setAttribute('aria-expanded', 'false');
  const auf = tastatur => kalenderOeffnen(knopf, { modus: modus === 'woche' ? 'woche' : 'tag', wert: gewaehlt, mitte: true, beiWahl, fokusSetzen: tastatur || undefined, ...weitere });
  knopf.onclick = ev => { if (offen?.anker === knopf) { offen.schliessen(false); return; } auf(ev.detail === 0); };
  knopf.addEventListener('keydown', ev => { if (ev.key === 'ArrowDown' && offen?.anker !== knopf) { ev.preventDefault(); auf(true); } });
}

// ------------------------------------------------------------------ Datumsfelder

const WERT = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
let selbst = false;

/** ISO-Wert prüfen: 'tag' → JJJJ-MM-TT, 'zeit' → JJJJ-MM-TTThh:mm; sonst ''. */
function normalisieren(v, typ) {
  v = String(v ?? '').trim();
  if (typ === 'zeit') {
    const m = /^(\d{4}-\d{2}-\d{2})[T ](\d{2}):(\d{2})/.exec(v);
    return m && gueltig(m[1]) && Number(m[2]) < 24 && Number(m[3]) < 60 ? `${m[1]}T${m[2]}:${m[3]}` : '';
  }
  return gueltig(v.slice(0, 10)) && (v.length === 10 || v[10] === 'T') ? v.slice(0, 10) : '';
}

const anzeige = (wert, typ) => !wert ? '' : typ === 'zeit' ? `${datumDe(wert.slice(0, 10))} ${wert.slice(11, 16)}` : datumDe(wert);

/** Eingetippten Text lesen: „17.10.2026“, „17.10.26“, „17.10.“, „2026-10-17“, optional mit Wochentag und Uhrzeit. null = ungültig. */
export function datumLesen(text, typ = 'tag', bisher = '') {
  let t = String(text ?? '').trim().replace(/^[A-Za-zÄÖÜäöü]{2,10}\.?,?\s+/, '');
  if (!t) return '';
  let zeit = '';
  const zm = /\s+(\d{1,2})[:.](\d{2})\s*(uhr)?$/i.exec(t);
  if (zm) { zeit = `${zm[1].padStart(2, '0')}:${zm[2]}`; t = t.slice(0, zm.index); }
  let d = null;
  let m = /^(\d{4})-(\d{1,2})-(\d{1,2})$/.exec(t);
  if (m) d = [Number(m[1]), Number(m[2]), Number(m[3])];
  m = m ? null : /^(\d{1,2})[./-](\d{1,2})(?:[./-](\d{2}|\d{4})?)?\.?$/.exec(t);
  if (m) {
    let j = m[3] ? Number(m[3]) : new Date().getFullYear();
    if (m[3]?.length === 2) j += 2000;
    d = [j, Number(m[2]), Number(m[1])];
  }
  m = d ? null : /^(\d{2})(\d{2})(\d{4})$/.exec(t);
  if (m) d = [Number(m[3]), Number(m[2]), Number(m[1])];
  if (!d) return null;
  const s = `${d[0]}-${String(d[1]).padStart(2, '0')}-${String(d[2]).padStart(2, '0')}`;
  if (!gueltig(s)) return null;
  if (typ !== 'zeit') return s;
  if (!zeit) zeit = bisher.slice(11, 16) || '08:00';
  const [hh, mm] = zeit.split(':').map(Number);
  return hh < 24 && mm < 60 ? `${s}T${zeit}` : null;
}

function melden(el) {
  selbst = true;
  try {
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
  } finally { selbst = false; }
}

// Getipptes erst beim Verlassen bzw. Enter übernehmen: Zwischenstände (input) und das native change sieht der übrige Code nicht
window.addEventListener('input', ev => {
  const el = ev.target;
  if (selbst || !el?.dataset?.kal || !el.__kal) return;
  ev.stopImmediatePropagation();
  el.__kal.tippen();
}, true);
window.addEventListener('change', ev => {
  const el = ev.target;
  if (selbst || !el?.dataset?.kal || !el.__kal) return;
  ev.stopImmediatePropagation();
  el.__kal.uebernehmen();
}, true);

/** Von/Bis-Partner eines Datumsfelds (gleicher Abschnitt, Namen bzw. data-Attribute mit „von“ ↔ „bis“). */
function partner(input) {
  const tausch = (s, von) => (von ? s.replace(/von/g, 'bis').replace(/Von/g, 'Bis').replace(/-vd$/, '-bd') : s.replace(/bis/g, 'von').replace(/Bis/g, 'Von').replace(/-bd$/, '-vd'));
  const kandidaten = [];
  const pruefen = (rolleVon, selektor) => kandidaten.push({ rolle: rolleVon ? 'von' : 'bis', selektor });
  for (const a of input.attributes) {
    if (!a.name.startsWith('data-') || a.name === 'data-kal') continue;
    if (/von|-vd$/.test(a.name)) pruefen(true, `[${tausch(a.name, true)}]`);
    else if (/bis|-bd$/.test(a.name)) pruefen(false, `[${tausch(a.name, false)}]`);
    else if (/^(von|bis)$/i.test(a.value)) pruefen(/^von$/i.test(a.value), `[${a.name}="${tausch(a.value, /^von$/i.test(a.value))}"]`);
  }
  if (input.name && /von|Von/.test(input.name)) pruefen(true, `[name="${tausch(input.name, true)}"]`);
  else if (input.name && /bis|Bis/.test(input.name)) pruefen(false, `[name="${tausch(input.name, false)}"]`);
  for (const k of kandidaten) {
    let el = input.parentElement;
    for (let i = 0; el && i < 5; i++, el = el.parentElement) {
      const p = [...el.querySelectorAll(`input[data-kal="tag"]${k.selektor}`)].find(x => x !== input && !x.disabled);
      if (p) return k.rolle === 'von' ? { rolle: 'von', von: input, bis: p } : { rolle: 'bis', von: p, bis: input };
    }
  }
  return null;
}

/** Ein Datumsfeld auf den eigenen Kalender umstellen (einmalig; weitere Aufrufe tun nichts). */
export function datumsfeld(input) {
  if (!input || input.dataset.kal || input.__kal) return;
  const typ = input.type === 'datetime-local' ? 'zeit' : 'tag';
  let wert = normalisieren(WERT.get.call(input), typ);
  input.dataset.kal = typ;
  try { input.type = 'text'; } catch { return; }
  input.classList.add('kal-eingabe');
  input.autocomplete = 'off';
  input.spellcheck = false;
  if (!input.placeholder) input.placeholder = typ === 'zeit' ? 'TT.MM.JJJJ hh:mm' : 'TT.MM.JJJJ';
  input.setAttribute('aria-haspopup', 'dialog');
  input.setAttribute('aria-expanded', 'false');
  if (!input.title) input.title = typ === 'zeit' ? 'Datum und Uhrzeit (TT.MM.JJJJ hh:mm) – Klick öffnet den Kalender' : 'Datum (TT.MM.JJJJ) – Klick öffnet den Kalender';
  if (grobZeiger()) input.inputMode = 'none';
  const zeigen = () => WERT.set.call(input, anzeige(wert, typ));
  Object.defineProperty(input, 'value', {
    configurable: true,
    get: () => wert,
    set: v => { wert = normalisieren(v, typ); zeigen(); },
  });
  const setzen = neu => { if (neu === wert) { zeigen(); return false; } wert = neu; zeigen(); return true; };

  const oeffnen = tastatur => {
    if (input.disabled || input.readOnly) return null;
    if (offen?.anker === input) return offen;
    const o = {
      min: input.min, max: input.max, fokusZurueck: input, fokusSetzen: tastatur,
      leeren: !input.required && !!wert, beiLeeren: () => { if (setzen('')) melden(input); },
    };
    const paar = typ === 'tag' ? partner(input) : null;
    if (typ === 'zeit') {
      Object.assign(o, { wert: wert.slice(0, 10), zeit: wert.slice(11, 16) || '08:00', beiWahl: (d, z) => { if (setzen(`${d}T${z}`)) melden(input); } });
    } else if (paar) {
      Object.assign(o, {
        modus: 'bereich', von: paar.von.value, bis: paar.bis.value, phase: paar.rolle,
        min: paar.von.min || input.min, max: paar.bis.max || input.max,
        beiWahl: ({ von, bis }) => {
          const vAlt = paar.von.value, bAlt = paar.bis.value;
          paar.von.value = von; paar.bis.value = bis;
          if (paar.von.value !== vAlt) melden(paar.von);
          if (paar.bis.value !== bAlt && paar.bis.isConnected) melden(paar.bis);
        },
      });
    } else Object.assign(o, { wert, beiWahl: d => { if (setzen(d)) melden(input); } });
    return kalenderOeffnen(input, o);
  };

  input.__kal = {
    oeffnen,
    tippen() {
      // Kalender beim Tippen mitführen
      const d = datumLesen(WERT.get.call(input), typ, wert);
      if (d && offen?.anker === input) offen.fokusAuf(d.slice(0, 10), true, false);
    },
    uebernehmen() {
      const text = WERT.get.call(input);
      const d = datumLesen(text, typ, wert);
      if (d === null) { zeigen(); return; }
      if (setzen(d)) melden(input);
    },
  };
  input.showPicker = () => { oeffnen(true); };
  input.addEventListener('click', () => { if (offen?.anker === input) return; oeffnen(false); });
  input.addEventListener('keydown', ev => {
    const auf = offen?.anker === input;
    if ((ev.key === 'ArrowDown' || (ev.altKey && ev.key === 'ArrowDown')) && !ev.ctrlKey && !ev.metaKey) {
      ev.preventDefault();
      if (auf) offen.fokusieren(); else oeffnen(true);
    } else if (ev.key === 'Escape' && auf) { ev.preventDefault(); ev.stopPropagation(); offen.schliessen(false); }
    else if (ev.key === 'Enter') {
      input.__kal.uebernehmen();
      if (auf) { ev.preventDefault(); offen.schliessen(false); }
    } else if (ev.key === 'Tab' && auf) offen.schliessen(false);
  });
  zeigen();
}

const AUSWAHL = 'input[type=date]:not([data-kal]):not([data-kal-nativ]):not([aria-hidden=true]), input[type=datetime-local]:not([data-kal]):not([data-kal-nativ]):not([aria-hidden=true])';

/** Alle Datumsfelder unterhalb von wurzel umstellen. */
export function datumsfelder(wurzel = document) {
  if (wurzel.matches?.(AUSWAHL)) datumsfeld(wurzel);
  wurzel.querySelectorAll?.(AUSWAHL).forEach(datumsfeld);
}

let beobachter = null;
/** Neu eingefügte Datumsfelder in der ganzen Seite automatisch umstellen (einmal beim Start aufrufen). */
export function beobachten(wurzel = document.body) {
  datumsfelder(wurzel);
  if (beobachter || !window.MutationObserver) return;
  beobachter = new MutationObserver(liste => {
    for (const m of liste) for (const n of m.addedNodes) if (n.nodeType === 1) datumsfelder(n);
  });
  beobachter.observe(wurzel, { childList: true, subtree: true });
}
