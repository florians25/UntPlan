// Virtualisierte Datentabelle (Stammdaten, Unterricht und Listen der Module).
//
// Spalten: [{ schluessel, titel, breite, art, editierbar, auswahl, refTyp, liste, ausrichtung, sichtbar, wert(zeile), farbe(zeile) }]
//   art: 'Text' | 'Ganzzahl' | 'Dezimal' | 'Bool' | 'Bereich' | 'Element' | 'ElementListe' | 'Farbe' | 'Auswahl' | 'Datum' | 'NurLesen'
// Optionen siehe Konstruktor. Tastatur wie im Desktop: Pfeile, Tab, Enter/F2 (bearbeiten), Esc, Entf (leeren), Leertaste (Haken),
// Strg+C/Strg+V (Kopieren/Einfügen als Tabulator-Text), Strg+A, Umschalt+Pfeil/Klick (Bereich), Strg+Klick (einzeln).
import { h, leeren, entprellen, vergleich, textbreite } from './dom.js';
import { Vorschlagsliste } from './auswahl.js';
import { menue, fehlerMelden, hinweis } from './dialog.js';
import { einstellungLesen, einstellungSchreiben } from './api.js';

const NEU = Object.freeze({ __neu: true, id: '__neu' });

export class Tabelle {
  /**
   * @param {HTMLElement} eltern
   * @param {object} o
   * @param {Array} o.spalten
   * @param {(z:any)=>any} [o.id] – Schlüssel einer Zeile (Standard z.id)
   * @param {number} [o.zeilenHoehe]
   * @param {string} [o.zustandSchluessel] – Benutzereinstellung für Spalten (Breite, Sichtbarkeit, Reihenfolge, Sortierung)
   * @param {(zeile, spalte, wert:string) => Promise<any>} [o.aendern] – liefert ggf. die neue Zeile
   * @param {(aenderungen:Array<{zeile,spalte,wert}>) => Promise<any>} [o.aendernMehrere]
   * @param {(spalte, wert:string) => Promise<any>} [o.neu] – Neuanlage-Zeile am Ende (liefert die neue Zeile)
   * @param {string} [o.neuText]
   * @param {(spalte) => Promise<Array<{k,l}>>} [o.optionen] – Auswahlliste für Element-Spalten
   * @param {(zeilen:Array, aktiv:any) => void} [o.beiAuswahl]
   * @param {(zeile, spalte) => void} [o.beiDoppelklick]
   * @param {(zeilen:Array, spalte) => Array} [o.kontextmenue]
   * @param {(zeile, index:number) => string} [o.zeilenKlasse]
   * @param {string} [o.leerText]
   */
  constructor(eltern, o) {
    this.o = { zeilenHoehe: 22, id: (z) => z.id, leerText: 'Keine Einträge', ...o };
    this.H = this.o.zeilenHoehe;
    this.alle = [];
    this.ansicht = [];
    this.gewaehlt = new Set();
    this.akt = { r: 0, c: 0 };
    this.anker = 0;
    this.sort = null;
    this.editor = null;
    this._bereich = [-1, -1];

    this.el = h('div', { class: 'tabelle', tabindex: '0' });
    this.kopfInnen = h('div', { class: 't-kopf-innen' });
    this.kopf = h('div', { class: 't-kopf' }, this.kopfInnen);
    this.platz = h('div', { class: 't-platz' });
    this.koerper = h('div', { class: 't-koerper' }, this.platz);
    this.leer = h('div', { class: 't-leer' });
    this.el.append(this.kopf, this.koerper, this.leer);
    eltern.append(this.el);

    this.spaltenSetzen(this.o.spalten || []);
    this.koerper.addEventListener('scroll', () => {
      this.kopfInnen.style.transform = `translateX(${-this.koerper.scrollLeft}px)`;
      this._planen();
    });
    this.koerper.addEventListener('pointerdown', (e) => this._klick(e));
    this.koerper.addEventListener('dblclick', (e) => this._doppelklick(e));
    this.koerper.addEventListener('contextmenu', (e) => this._kontext(e));
    this.el.addEventListener('keydown', (e) => this._taste(e));
    this.el.addEventListener('copy', (e) => this._kopieren(e));
    this.el.addEventListener('paste', (e) => this._einfuegen(e));
    this.kopf.addEventListener('contextmenu', (e) => { e.preventDefault(); this.spaltenMenue(e.clientX, e.clientY); });
    this._beobachter = new ResizeObserver(() => this._planen());
    this._beobachter.observe(this.koerper);
    this._speichern = entprellen(() => this._zustandSpeichern(), 600);
    if (this.o.zustandSchluessel) this._zustandLaden();
  }

  // ------------------------------------------------------------------------------------------
  // Spalten

  spaltenSetzen(spalten) {
    const alt = new Map((this.spalten || []).map((s) => [s.schluessel, s]));
    this.spalten = spalten.map((s) => {
      const a = alt.get(s.schluessel);
      return { breite: 80, sichtbar: s.standard !== false, ...s, ...(a ? { breite: a.breite, sichtbar: a.sichtbar } : {}) };
    });
    if (this._gespeicherterZustand) this._zustandAnwenden(this._gespeicherterZustand);
    this._kopfZeichnen();
    this._sortieren();
    this._neuZeichnen();
  }

  get sichtbar() { return this.spalten.filter((s) => s.sichtbar); }

  _kopfZeichnen() {
    leeren(this.kopfInnen);
    let x = 0;
    for (const s of this.sichtbar) {
      const zelle = h('div', { class: 't-kopfzelle', style: { width: `${s.breite}px` }, title: s.hinweis || s.titel, draggable: 'true' },
        h('span', { class: 't-titel' }, s.titel),
        this.sort?.schluessel === s.schluessel ? h('span', { class: 't-sort' }, this.sort.ab ? '▼' : '▲') : null,
        h('span', { class: 't-breite' }));
      zelle.querySelector('.t-breite').addEventListener('pointerdown', (e) => this._breiteZiehen(e, s, zelle));
      zelle.addEventListener('click', (e) => { if (!e.target.classList.contains('t-breite')) this.sortieren(s.schluessel); });
      zelle.addEventListener('dragstart', (e) => { e.dataTransfer.setData('text/x-spalte', s.schluessel); e.dataTransfer.effectAllowed = 'move'; });
      zelle.addEventListener('dragover', (e) => { if (e.dataTransfer.types.includes('text/x-spalte')) { e.preventDefault(); zelle.classList.add('zieht'); } });
      zelle.addEventListener('dragleave', () => zelle.classList.remove('zieht'));
      zelle.addEventListener('drop', (e) => {
        e.preventDefault();
        zelle.classList.remove('zieht');
        this.spalteVerschieben(e.dataTransfer.getData('text/x-spalte'), s.schluessel);
      });
      this.kopfInnen.append(zelle);
      x += s.breite;
    }
    this.gesamtbreite = x;
    this.kopfInnen.style.width = `${x}px`;
    this.platz.style.width = `${x}px`;
  }

  _breiteZiehen(e, s, zelle) {
    e.preventDefault();
    e.stopPropagation();
    const start = e.clientX, b0 = s.breite;
    const bewegen = (ev) => {
      s.breite = Math.max(24, Math.round(b0 + ev.clientX - start));
      zelle.style.width = `${s.breite}px`;
    };
    const ende = () => {
      removeEventListener('pointermove', bewegen);
      removeEventListener('pointerup', ende);
      this._kopfZeichnen();
      this._neuZeichnen();
      this._speichern();
    };
    addEventListener('pointermove', bewegen);
    addEventListener('pointerup', ende);
  }

  spalteVerschieben(von, nach) {
    if (!von || von === nach) return;
    const i = this.spalten.findIndex((s) => s.schluessel === von);
    const [s] = this.spalten.splice(i, 1);
    this.spalten.splice(this.spalten.findIndex((x) => x.schluessel === nach), 0, s);
    this._kopfZeichnen();
    this._neuZeichnen();
    this._speichern();
  }

  /** Spaltenbreiten an den Inhalt anpassen (wie „Alle Spalten anpassen“). */
  spaltenAnpassen() {
    for (const s of this.sichtbar) {
      let max = textbreite(s.titel, '500 12.5px system-ui') + 22;
      for (let i = 0; i < Math.min(this.ansicht.length, 2000); i++) max = Math.max(max, textbreite(this._text(this.ansicht[i], s)) + 16);
      s.breite = Math.min(420, Math.ceil(max));
    }
    this._kopfZeichnen();
    this._neuZeichnen();
    this._speichern();
  }

  spaltenMenue(x, y) {
    const karten = [...new Set(this.spalten.map((s) => s.karteikarte || s.karte || ''))];
    const eintraege = [
      { text: 'Alle Spalten anpassen', symbol: 'spalten', aktion: () => this.spaltenAnpassen() },
      { text: 'Standardspalten', aktion: () => { for (const s of this.spalten) s.sichtbar = s.standard !== false; this._kopfZeichnen(); this._neuZeichnen(); this._speichern(); } },
    ];
    if (this.sort) eintraege.push({ text: 'Sortierung aufheben', symbol: 'sortieren', aktion: () => { this.sort = null; this._sortieren(); this._kopfZeichnen(); this._neuZeichnen(); this._speichern(); } });
    eintraege.push(null, { titel: 'Spalten ein-/ausblenden' });
    for (const k of karten) {
      const liste = this.spalten.filter((s) => (s.karteikarte || s.karte || '') === k);
      const punkte = liste.map((s) => ({
        text: s.titel, haken: s.sichtbar,
        aktion: () => { s.sichtbar = !s.sichtbar; if (!this.sichtbar.length) s.sichtbar = true; this._kopfZeichnen(); this._neuZeichnen(); this._speichern(); },
      }));
      if (karten.length > 1) eintraege.push({ text: k || 'Allgemein', menue: punkte });
      else eintraege.push(...punkte);
    }
    menue(x, y, eintraege);
  }

  // ------------------------------------------------------------------------------------------
  // Daten

  /** Daten setzen; Auswahl und aktive Zelle bleiben (über die Id) erhalten. */
  daten(zeilen) {
    const aktId = this._zeile(this.akt.r) ? this.o.id(this._zeile(this.akt.r)) : null;
    this.alle = zeilen || [];
    const ids = new Set(this.alle.map(this.o.id));
    for (const id of [...this.gewaehlt]) if (!ids.has(id)) this.gewaehlt.delete(id);
    this._sortieren();
    if (aktId !== null) {
      const r = this.ansicht.findIndex((z) => this.o.id(z) === aktId);
      if (r >= 0) this.akt.r = r;
    }
    this.akt.r = Math.max(0, Math.min(this.akt.r, this._anzahl - 1));
    this._neuZeichnen();
  }

  get zeilen() { return this.alle; }

  /** Eine Zeile ersetzen (z. B. nach dem Speichern eines Wertes). */
  zeileErsetzen(id, neu) {
    const i = this.alle.findIndex((z) => this.o.id(z) === id);
    if (i >= 0) this.alle[i] = neu; else this.alle.push(neu);
    const j = this.ansicht.findIndex((z) => this.o.id(z) === id);
    if (j >= 0) this.ansicht[j] = neu; else this._sortieren();
    this._neuZeichnen();
  }

  ausgewaehlte() {
    if (this.gewaehlt.size) return this.ansicht.filter((z) => this.gewaehlt.has(this.o.id(z)));
    const z = this._zeile(this.akt.r);
    return z && !z.__neu ? [z] : [];
  }

  aktiveZeile() { const z = this._zeile(this.akt.r); return z?.__neu ? null : z; }
  aktiveSpalte() { return this.sichtbar[this.akt.c]; }

  /** Zeile mit dieser Id wählen und sichtbar machen. */
  waehle(id, spalte) {
    const r = id === NEU.id && this.o.neu ? this.ansicht.length : this.ansicht.findIndex((z) => this.o.id(z) === id);
    if (r < 0) return false;
    const c = spalte && this.sichtbar.some((s) => s.schluessel === spalte) ? this.sichtbar.findIndex((s) => s.schluessel === spalte) : this.akt.c;
    this._gehe(r, c, false);
    return true;
  }

  fokus() { if (!this.editor) this.el.focus({ preventScroll: true }); }

  sortieren(schluessel) {
    if (this.o.sortierbar === false) return;
    if (this.sort?.schluessel === schluessel) this.sort = this.sort.ab ? null : { schluessel, ab: true };
    else this.sort = { schluessel, ab: false };
    this._sortieren();
    this._kopfZeichnen();
    this._neuZeichnen();
    this._speichern();
  }

  _sortieren() {
    const s = this.sort && this.spalten.find((x) => x.schluessel === this.sort.schluessel);
    this.ansicht = this.alle.slice();
    if (s && !this.o.gruppiert) {
      const f = this.sort.ab ? -1 : 1;
      this.ansicht.sort((a, b) => f * vergleich(this._text(a, s), this._text(b, s)));
    } else if (s && this.o.gruppiert) {
      // gruppierte Zeilen (z. B. Kopplungszeilen eines Unterrichts) bleiben zusammen: nach der ersten Zeile der Gruppe sortieren
      const gruppen = new Map();
      for (const z of this.alle) { const g = this.o.gruppiert(z); if (!gruppen.has(g)) gruppen.set(g, []); gruppen.get(g).push(z); }
      const f = this.sort.ab ? -1 : 1;
      this.ansicht = [...gruppen.values()].sort((a, b) => f * vergleich(this._text(a[0], s), this._text(b[0], s))).flat();
    }
  }

  get _anzahl() { return this.ansicht.length + (this.o.neu ? 1 : 0); }
  _zeile(r) { return r < this.ansicht.length ? this.ansicht[r] : (this.o.neu && r === this.ansicht.length ? NEU : null); }
  _text(z, s) {
    if (!z || z.__neu) return '';
    const v = s.wert ? s.wert(z) : z[s.schluessel];
    return v === null || v === undefined ? '' : String(v);
  }

  // ------------------------------------------------------------------------------------------
  // Zeichnen (nur sichtbare Zeilen)

  _planen() {
    if (this._rahmen) return;
    this._rahmen = requestAnimationFrame(() => { this._rahmen = 0; this._zeichnen(false); });
  }

  _neuZeichnen() { this._zeichnen(true); }

  _zeichnen(erzwingen) {
    const n = this._anzahl;
    this.platz.style.height = `${n * this.H}px`;
    this.leer.textContent = this.ansicht.length === 0 && !this.o.neu ? this.o.leerText : '';
    const oben = this.koerper.scrollTop, hoehe = this.koerper.clientHeight || 400;
    const von = Math.max(0, Math.floor(oben / this.H) - 8);
    const bis = Math.min(n, Math.ceil((oben + hoehe) / this.H) + 8);
    if (!erzwingen && von === this._bereich[0] && bis === this._bereich[1]) return;
    this._bereich = [von, bis];
    for (const alt of [...this.platz.children]) if (alt.classList.contains('t-zeile')) alt.remove(); // Editor bleibt stehen
    const spalten = this.sichtbar;
    const frag = document.createDocumentFragment();
    for (let r = von; r < bis; r++) frag.append(this._zeileElement(r, spalten));
    this.platz.prepend(frag);
  }

  _zeileElement(r, spalten) {
    const z = this._zeile(r);
    const neu = !!z.__neu;
    const id = neu ? NEU.id : this.o.id(z);
    const klassen = ['t-zeile'];
    if (neu) klassen.push('t-neu');
    else {
      if (r % 2) klassen.push('wechsel');
      if (this.gewaehlt.has(id)) klassen.push('gewaehlt');
      const k = this.o.zeilenKlasse?.(z, r);
      if (k) klassen.push(k);
    }
    const zeile = h('div', { class: klassen.join(' '), style: { top: `${r * this.H}px`, height: `${this.H}px`, width: `${this.gesamtbreite}px` } });
    zeile.dataset.r = r;
    spalten.forEach((s, c) => {
      const text = neu ? (c === this._neuSpalte() ? (this.o.neuText || 'Neu …') : '') : this._text(z, s);
      const kl = ['t-zelle'];
      if (s.art === 'Bool') kl.push('mitte');
      else if (s.ausrichtung === 'rechts' || s.art === 'Ganzzahl' || s.art === 'Dezimal') kl.push('rechts');
      if (!neu && (s.art === 'NurLesen' || s.editierbar === false)) kl.push('nurlesen');
      if (r === this.akt.r && c === this.akt.c) kl.push('aktiv');
      const zk = !neu && s.klasse?.(z); // zusätzliche CSS-Klasse je Zelle (z. B. offene Vertretung rot)
      if (zk) kl.push(zk);
      const zelle = h('div', { class: kl.join(' '), style: { width: `${s.breite}px` } });
      if (!neu && s.art === 'Bool') zelle.append(h('span', { class: `t-haken${text === 'X' || text === 'true' ? ' an' : ''}` }));
      else if (!neu && s.art === 'Farbe' && text) zelle.append(h('span', { class: 'farbpunkt', style: { background: text } }), h('span', null, text));
      else {
        const span = h('span', null, text);
        if (text.length > 12) span.title = text;
        zelle.append(span);
      }
      const farbe = !neu && s.farbe?.(z);
      if (farbe) zelle.style.background = farbe;
      zeile.append(zelle);
    });
    return zeile;
  }

  _neuSpalte() {
    const i = this.sichtbar.findIndex((s) => s.editierbar !== false && s.art !== 'NurLesen' && s.art !== 'Bool');
    return i < 0 ? 0 : i;
  }

  _aktivMarkieren() {
    for (const el of this.platz.querySelectorAll('.t-zelle.aktiv')) el.classList.remove('aktiv');
    for (const el of this.platz.querySelectorAll('.t-zeile.gewaehlt')) el.classList.remove('gewaehlt');
    for (const zeile of this.platz.querySelectorAll('.t-zeile')) {
      const r = Number(zeile.dataset.r);
      const z = this._zeile(r);
      if (z && !z.__neu && this.gewaehlt.has(this.o.id(z))) zeile.classList.add('gewaehlt');
      if (r === this.akt.r) zeile.children[this.akt.c]?.classList.add('aktiv');
    }
  }

  _sichtbarMachen() {
    const y = this.akt.r * this.H;
    const k = this.koerper;
    if (y < k.scrollTop) k.scrollTop = y;
    else if (y + this.H > k.scrollTop + k.clientHeight) k.scrollTop = y + this.H - k.clientHeight;
    let x = 0;
    const sp = this.sichtbar;
    for (let i = 0; i < this.akt.c; i++) x += sp[i].breite;
    const b = sp[this.akt.c]?.breite || 0;
    if (x < k.scrollLeft) k.scrollLeft = x;
    else if (x + b > k.scrollLeft + k.clientWidth) k.scrollLeft = x + b - k.clientWidth;
  }

  // ------------------------------------------------------------------------------------------
  // Auswahl und Navigation

  _gehe(r, c, erweitern, einzeln = false) {
    const n = this._anzahl;
    if (n === 0) return;
    r = Math.max(0, Math.min(n - 1, r));
    c = Math.max(0, Math.min(this.sichtbar.length - 1, c));
    this.akt = { r, c };
    if (erweitern) {
      this.gewaehlt.clear();
      const [a, b] = [Math.min(this.anker, r), Math.max(this.anker, r)];
      for (let i = a; i <= b; i++) { const z = this._zeile(i); if (z && !z.__neu) this.gewaehlt.add(this.o.id(z)); }
    } else if (einzeln) {
      const z = this._zeile(r);
      if (z && !z.__neu) { const id = this.o.id(z); if (this.gewaehlt.has(id)) this.gewaehlt.delete(id); else this.gewaehlt.add(id); }
      this.anker = r;
    } else {
      this.gewaehlt.clear();
      this.anker = r;
    }
    this._sichtbarMachen();
    this._zeichnen(false);
    this._aktivMarkieren();
    this.o.beiAuswahl?.(this.ausgewaehlte(), this.aktiveZeile());
  }

  _zelleBei(e) {
    const zeile = e.target.closest('.t-zeile');
    if (!zeile) return null;
    const zelle = e.target.closest('.t-zelle');
    return { r: Number(zeile.dataset.r), c: zelle ? [...zeile.children].indexOf(zelle) : this.akt.c };
  }

  _klick(e) {
    if (e.target.closest('.t-editor')) return;
    const p = this._zelleBei(e);
    if (!p) return;
    if (this.editor) this._editorBeenden(true);
    if (e.button === 2 && this.gewaehlt.size > 1 && this.gewaehlt.has(this.o.id(this._zeile(p.r)))) { this.akt = p; this._aktivMarkieren(); return; }
    this._gehe(p.r, p.c, e.shiftKey, e.ctrlKey || e.metaKey);
    // Haken direkt per Klick umschalten
    const s = this.sichtbar[p.c], z = this._zeile(p.r);
    if (e.button === 0 && !e.shiftKey && !e.ctrlKey && s?.art === 'Bool' && z && !z.__neu && e.target.closest('.t-haken') && this._editierbar(s, z)) {
      this._speichernWert(z, s, this._text(z, s) === 'X' ? '' : 'X');
    }
  }

  _doppelklick(e) {
    const p = this._zelleBei(e);
    if (!p) return;
    const z = this._zeile(p.r), s = this.sichtbar[p.c];
    if (this.o.beiDoppelklick && z && !z.__neu && (!this._editierbar(s, z) || s.art === 'Bool')) { this.o.beiDoppelklick(z, s); return; }
    this.bearbeiten();
  }

  _kontext(e) {
    e.preventDefault();
    if (!this.o.kontextmenue) return;
    const p = this._zelleBei(e);
    if (p && !this.gewaehlt.has(this.o.id(this._zeile(p.r) || NEU))) this._gehe(p.r, p.c, false);
    const eintraege = this.o.kontextmenue(this.ausgewaehlte(), this.aktiveSpalte());
    if (eintraege?.length) menue(e.clientX, e.clientY, eintraege);
  }

  _taste(e) {
    if (this.editor) return; // Editor behandelt seine Tasten selbst
    const { r, c } = this.akt;
    const seite = Math.max(1, Math.floor(this.koerper.clientHeight / this.H) - 1);
    const strg = e.ctrlKey || e.metaKey;
    const z = this._zeile(r), s = this.sichtbar[c];
    switch (e.key) {
      case 'ArrowDown': this._gehe(r + 1, c, e.shiftKey); break;
      case 'ArrowUp': this._gehe(r - 1, c, e.shiftKey); break;
      case 'ArrowRight': this._gehe(r, c + 1, false); break;
      case 'ArrowLeft': this._gehe(r, c - 1, false); break;
      case 'PageDown': this._gehe(r + seite, c, e.shiftKey); break;
      case 'PageUp': this._gehe(r - seite, c, e.shiftKey); break;
      case 'Home': this._gehe(strg ? 0 : r, 0, false); break;
      case 'End': this._gehe(strg ? this._anzahl - 1 : r, this.sichtbar.length - 1, false); break;
      case 'Tab':
        if (e.shiftKey) { if (c > 0) this._gehe(r, c - 1, false); else this._gehe(r - 1, this.sichtbar.length - 1, false); }
        else if (c < this.sichtbar.length - 1) this._gehe(r, c + 1, false); else this._gehe(r + 1, 0, false);
        break;
      case 'Enter': case 'F2':
        if (z && this._editierbar(s, z) && s.art !== 'Bool') this.bearbeiten();
        else if (e.key === 'Enter') this._gehe(r + (e.shiftKey ? -1 : 1), c, false);
        break;
      case ' ':
        if (s?.art === 'Bool' && z && !z.__neu && this._editierbar(s, z)) this._speichernWert(z, s, this._text(z, s) === 'X' ? '' : 'X');
        else return;
        break;
      case 'Delete': case 'Backspace':
        if (z && !z.__neu && this._editierbar(s, z) && s.art !== 'Bool' && this._text(z, s) !== '') this._speichernWert(z, s, '');
        else if (e.key === 'Delete' && this.o.beiEntf) this.o.beiEntf(this.ausgewaehlte());
        break;
      case 'a': case 'A':
        if (!strg) return this._tippen(e);
        for (const x of this.ansicht) this.gewaehlt.add(this.o.id(x));
        this._aktivMarkieren();
        this.o.beiAuswahl?.(this.ausgewaehlte(), this.aktiveZeile());
        break;
      default:
        return this._tippen(e);
    }
    e.preventDefault();
  }

  _tippen(e) {
    if (e.key.length !== 1 || e.ctrlKey || e.metaKey || e.altKey) return;
    const z = this._zeile(this.akt.r), s = this.sichtbar[this.akt.c];
    if (!z || !this._editierbar(s, z) || s.art === 'Bool') return;
    e.preventDefault();
    this.bearbeiten(e.key);
  }

  _editierbar(s, z) {
    if (!s || s.art === 'NurLesen' || s.editierbar === false) return false;
    if (z?.__neu) return !!this.o.neu;
    if (typeof s.editierbarFuer === 'function') return s.editierbarFuer(z);
    return !!(this.o.aendern || s.aendern);
  }

  // ------------------------------------------------------------------------------------------
  // Bearbeiten

  /** Aktive Zelle bearbeiten (optional mit erstem getipptem Zeichen). */
  bearbeiten(start) {
    const z = this._zeile(this.akt.r), s = this.sichtbar[this.akt.c];
    if (!z || !this._editierbar(s, z)) return;
    this._sichtbarMachen();
    let x = 0;
    for (let i = 0; i < this.akt.c; i++) x += this.sichtbar[i].breite;
    const alt = this._text(z, s);
    let el;
    if (s.art === 'Auswahl' && s.auswahl?.length) {
      el = h('select', { class: 't-editor' }, h('option', { value: '' }, ''), s.auswahl.map((a) => h('option', { value: a, selected: a === alt }, a)));
      el.addEventListener('change', () => this._editorBeenden(true));
    } else {
      el = h('input', { class: 't-editor', type: 'text', value: start ?? alt, spellcheck: 'false' });
    }
    Object.assign(el.style, { left: `${x}px`, top: `${this.akt.r * this.H}px`, width: `${Math.max(s.breite, s.art === 'ElementListe' ? 160 : 60)}px`, height: `${this.H + 1}px` });
    this.platz.append(el);
    const ed = { el, zeile: z, spalte: s, alt, vorschlaege: null };
    if ((s.art === 'Element' || s.art === 'ElementListe') && this.o.optionen) {
      ed.vorschlaege = new Vorschlagsliste(el, { optionen: () => this.o.optionen(s), liste: s.art === 'ElementListe' });
    }
    this.editor = ed;
    el.addEventListener('keydown', (e) => this._editorTaste(e));
    el.addEventListener('blur', () => setTimeout(() => { if (this.editor === ed && document.activeElement !== el) this._editorBeenden(true); }, 150));
    el.focus();
    if (el instanceof HTMLInputElement) {
      if (start === undefined) el.select(); else el.setSelectionRange(el.value.length, el.value.length);
      if (ed.vorschlaege && start !== undefined) ed.vorschlaege.zeigen();
    }
  }

  _editorTaste(e) {
    const ed = this.editor;
    if (!ed) return;
    if (ed.vorschlaege?.taste(e)) return;
    const { r, c } = this.akt;
    const weiter = (dr, dc) => { this._editorBeenden(true).then(() => { if (dr || dc) this._gehe(r + dr, c + dc, false); this.fokus(); }); };
    if (e.key === 'Enter') { e.preventDefault(); weiter(e.shiftKey ? -1 : 1, 0); }
    else if (e.key === 'Tab') { e.preventDefault(); weiter(0, e.shiftKey ? -1 : 1); }
    else if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); this._editorBeenden(false); this.fokus(); }
    else if ((e.key === 'ArrowDown' || e.key === 'ArrowUp') && ed.el instanceof HTMLInputElement) { e.preventDefault(); weiter(e.key === 'ArrowDown' ? 1 : -1, 0); }
    e.stopPropagation();
  }

  async _editorBeenden(uebernehmen) {
    const ed = this.editor;
    if (!ed) return;
    this.editor = null;
    ed.vorschlaege?.entfernen();
    const wert = ed.el.value;
    ed.el.remove();
    if (uebernehmen && wert !== ed.alt) await this._speichernWert(ed.zeile, ed.spalte, wert);
  }

  async _speichernWert(z, s, wert) {
    try {
      let neu;
      if (z.__neu) {
        neu = await this.o.neu(s, wert);
        if (neu) {
          this.alle.push(neu);
          this._sortieren();
          this._neuZeichnen();
          this.waehle(this.o.id(neu), s.schluessel);
        }
        return;
      }
      neu = await (s.aendern ? s.aendern(z, wert) : this.o.aendern(z, s, wert));
      if (neu && typeof neu === 'object' && !Array.isArray(neu)) this.zeileErsetzen(this.o.id(z), neu);
      else this._neuZeichnen();
    } catch (f) {
      fehlerMelden(f);
      this._neuZeichnen();
    }
  }

  // ------------------------------------------------------------------------------------------
  // Zwischenablage

  _kopieren(e) {
    if (this.editor) return;
    const zeilen = this.gewaehlt.size > 1 ? this.ausgewaehlte() : null;
    const text = zeilen
      ? zeilen.map((z) => this.sichtbar.map((s) => this._text(z, s)).join('\t')).join('\r\n')
      : this._text(this._zeile(this.akt.r), this.sichtbar[this.akt.c]);
    e.clipboardData.setData('text/plain', text);
    e.preventDefault();
    hinweis(zeilen ? `${zeilen.length} Zeilen kopiert.` : 'Zelle kopiert.', 'info', 1500);
  }

  async _einfuegen(e) {
    if (this.editor) return;
    const text = e.clipboardData.getData('text/plain');
    if (!text) return;
    e.preventDefault();
    const zeilen = text.replace(/\r/g, '').split('\n');
    if (zeilen.length && zeilen[zeilen.length - 1] === '') zeilen.pop();
    const aenderungen = [];
    zeilen.forEach((zeile, i) => {
      const z = this._zeile(this.akt.r + i);
      if (!z || z.__neu) return;
      zeile.split('\t').forEach((wert, j) => {
        const s = this.sichtbar[this.akt.c + j];
        if (s && this._editierbar(s, z) && this._text(z, s) !== wert) aenderungen.push({ zeile: z, spalte: s, wert });
      });
    });
    if (!aenderungen.length) return;
    try {
      if (this.o.aendernMehrere) await this.o.aendernMehrere(aenderungen);
      else for (const a of aenderungen) await this._speichernWert(a.zeile, a.spalte, a.wert);
      hinweis(`${aenderungen.length} Werte eingefügt.`, 'ok', 2000);
    } catch (f) { fehlerMelden(f); }
  }

  // ------------------------------------------------------------------------------------------
  // Spaltenzustand speichern/laden (Benutzereinstellung)

  _zustand() {
    return { spalten: this.spalten.map((s) => ({ s: s.schluessel, b: s.breite, v: s.sichtbar })), sort: this.sort };
  }

  _zustandAnwenden(z) {
    if (!z?.spalten) return;
    const map = new Map(z.spalten.map((x, i) => [x.s, { ...x, i }]));
    for (const s of this.spalten) {
      const g = map.get(s.schluessel);
      if (g) { s.breite = g.b || s.breite; s.sichtbar = !!g.v; }
    }
    this.spalten.sort((a, b) => (map.get(a.schluessel)?.i ?? 999) - (map.get(b.schluessel)?.i ?? 999));
    if (!this.sichtbar.length && this.spalten[0]) this.spalten[0].sichtbar = true;
    if (z.sort && this.spalten.some((s) => s.schluessel === z.sort.schluessel)) this.sort = z.sort;
  }

  async _zustandLaden() {
    const z = await einstellungLesen(this.o.zustandSchluessel);
    if (!z) return;
    this._gespeicherterZustand = z;
    this._zustandAnwenden(z);
    this._kopfZeichnen();
    this._sortieren();
    this._neuZeichnen();
  }

  _zustandSpeichern() {
    if (!this.o.zustandSchluessel) return;
    this._gespeicherterZustand = this._zustand();
    einstellungSchreiben(this.o.zustandSchluessel, this._gespeicherterZustand);
  }

  entfernen() {
    this._beobachter.disconnect();
    this.editor?.vorschlaege?.entfernen();
    this.el.remove();
  }
}
