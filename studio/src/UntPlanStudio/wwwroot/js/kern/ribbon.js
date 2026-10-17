// Menüband (Ribbon) mit Reitern, Gruppen, großen und kleinen Knöpfen – Aufbau wie in der Desktop-Anwendung.
// Die Einträge kommen aus den Modulen (modulsystem.js). Reiter/Gruppen in REITER bestimmen die Reihenfolge; unbekannte
// Reiter bzw. Gruppen werden hinten angefügt.
import { h, leeren } from './dom.js';
import { symbol } from './symbole.js';
import { an } from './ereignisse.js';
import { zustand } from './zustand.js';
import { menue } from './dialog.js';
import { alleRibbonEintraege, verfuegbar, eintragAusfuehren, befehl } from './modulsystem.js';
import { einstellungLesen, einstellungSchreiben } from './api.js';

/** Reiter mit Gruppenreihenfolge (modus: nur in diesem Modus sichtbar). */
export const REITER = [
  { name: 'Start', gruppen: ['Übersicht', 'Planungswerkzeuge', 'Schule', 'Module', 'Werkzeuge', 'Fenster'] },
  { name: 'Dateneingabe', gruppen: ['Stammdaten', 'Spezielle Daten', 'Unterricht', 'Zeit', 'Hilfsmittel'] },
  { name: 'Planung', modus: 'stundenplan', gruppen: ['Manuelles Planen', 'Stundenpläne', 'Werkzeuge'] },
  { name: 'Stundenpläne', gruppen: ['Einzelpläne', 'Formate', 'Übersichtspläne', 'Ausgabe'] },
  { name: 'Kursplanung', modus: 'stundenplan', gruppen: ['Kursplanung'] },
  { name: 'Vertretungsplanung', modus: 'vertretung', gruppen: ['Kalender', 'Vertretungsplanung', 'Ereignisse', 'Planung', 'Stundenpläne', 'Einstellungen', 'Modus'] },
  { name: 'Module', modus: 'stundenplan', gruppen: ['Unterrichtsplanung und Wertrechnung', 'Weitere Module'] },
];

export class Ribbon {
  constructor(eltern) {
    this.aktiv = 'Start';
    this.reiterleiste = h('div', { class: 'reiterleiste', role: 'tablist' });
    this.band = h('div', { class: 'band', role: 'toolbar' });
    this.el = h('div', { class: 'ribbon' }, this.reiterleiste, this.band);
    eltern.append(this.el);
    for (const name of ['stand', 'dokument', 'modus']) an(name, () => this.aktualisieren());
    an('modus', (m) => {
      if (m === 'vertretung') this.aktiv = 'Vertretungsplanung';
      else if (this.aktiv === 'Vertretungsplanung') this.aktiv = 'Start';
      this.zeichnen();
    });
    einstellungLesen('ribbon.eingeklappt').then((w) => { if (w) this.el.classList.add('eingeklappt'); });
  }

  /** Reiter in Anzeigereihenfolge (nur sichtbare). */
  reiter() {
    const eintraege = alleRibbonEintraege();
    const namen = REITER.filter((r) => !r.modus || r.modus === zustand.modus).map((r) => r.name);
    for (const e of eintraege) if (!namen.includes(e.reiter) && !REITER.some((r) => r.name === e.reiter)) namen.push(e.reiter);
    return namen.filter((n) => eintraege.some((e) => e.reiter === n));
  }

  zeichnen() {
    leeren(this.reiterleiste);
    this.reiterleiste.append(h('button', { class: 'reiter datei', type: 'button', onclick: () => befehl('datei.backstage') }, 'Datei'));
    const reiter = this.reiter();
    if (!reiter.includes(this.aktiv)) this.aktiv = reiter[0];
    for (const name of reiter) {
      const modus = REITER.find((r) => r.name === name)?.modus === 'vertretung';
      this.reiterleiste.append(h('button', {
        class: `reiter${name === this.aktiv ? ' aktiv' : ''}${modus ? ' modus' : ''}`, type: 'button', role: 'tab',
        onclick: () => {
          if (this.el.classList.contains('eingeklappt')) { this.el.classList.remove('eingeklappt'); einstellungSchreiben('ribbon.eingeklappt', false); }
          this.aktiv = name;
          this.zeichnen();
        },
        ondblclick: () => { const ein = this.el.classList.toggle('eingeklappt'); einstellungSchreiben('ribbon.eingeklappt', ein); },
      }, name));
    }
    this.reiterleiste.append(h('div', { class: 'rechts' },
      h('button', { type: 'button', title: 'Menüband ein-/ausklappen (Doppelklick auf einen Reiter)', onclick: () => { const ein = this.el.classList.toggle('eingeklappt'); einstellungSchreiben('ribbon.eingeklappt', ein); } }, symbol('pfeil_runter'))));
    this._bandZeichnen();
  }

  _bandZeichnen() {
    leeren(this.band);
    const eintraege = alleRibbonEintraege().filter((e) => e.reiter === this.aktiv);
    const def = REITER.find((r) => r.name === this.aktiv);
    const gruppen = [...(def?.gruppen || [])];
    for (const e of eintraege) if (!gruppen.includes(e.gruppe)) gruppen.push(e.gruppe);
    this.knoepfe = [];
    for (const g of gruppen) {
      const liste = eintraege.filter((e) => e.gruppe === g);
      if (!liste.length) continue;
      const inhalt = h('div', { class: 'eintraege' });
      let spalte = null;
      for (const e of liste) {
        // eigenes Steuerelement (z. B. Datumsauswahl der Vertretungsplanung): e.element() liefert das DOM-Element
        const k = e.element ? e.element() : this._knopf(e);
        if (e.gross) { inhalt.append(k); spalte = null; continue; }
        if (!spalte || spalte.childElementCount >= 3) { spalte = h('div', { class: 'spalte' }); inhalt.append(spalte); }
        spalte.append(k);
      }
      this.band.append(h('div', { class: 'gruppe', role: 'group', 'aria-label': g }, inhalt, h('div', { class: 'gruppentitel' }, g)));
    }
    this.aktualisieren();
  }

  _knopf(e) {
    const k = h('button', {
      class: `rb-knopf ${e.gross ? 'gross' : 'klein'}${e.geplant ? ' geplant' : ''}`, type: 'button',
      title: [e.hinweis || e.text, e.taste ? `(${e.taste})` : '', e.geplant ? '– folgt in einer späteren Ausbaustufe' : ''].filter(Boolean).join(' '),
      dataset: { id: e.id },
    }, symbol(e.symbol || 'modul'), h('span', { class: 'text' }, e.text), e.menue ? h('span', { class: 'pfeil' }, '▾') : null);
    k.addEventListener('click', () => {
      if (e.menue) {
        const r = k.getBoundingClientRect();
        menue(r.left, r.bottom + 2, e.menue.map((m) => (m ? { ...m, deaktiviert: !verfuegbar({ braucht: e.braucht, ...m }), aktion: () => eintragAusfuehren(m) } : null)));
      } else eintragAusfuehren(e);
    });
    this.knoepfe.push({ k, e });
    return k;
  }

  /** Aktiviert/deaktiviert die Knöpfe nach Dokument, Rechten und Modus. */
  aktualisieren() {
    for (const { k, e } of this.knoepfe || []) {
      k.disabled = !verfuegbar(e);
      k.classList.toggle('gedrueckt', !!e.gedrueckt?.());
    }
  }

  /** Reiter wählen (z. B. von außen). */
  zeige(name) {
    this.aktiv = name;
    this.zeichnen();
  }
}
