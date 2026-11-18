// Zeitraum wie im Original: zwei Datumsfelder und daneben die Auswahl „Aktuelle Woche“, „Aktueller Monat“, „Bis Schuljahresende“,
// „Aktuelles Schuljahr“ bzw. „Datumsbereich“ (frei gewählte Daten). Gemeinsam für Abwesenheiten, Klassendienste, Klassenbucheinträge,
// Berichte und Unterricht → Klasse. Bausteine: sg-feld (Beschriftung links), nl-pille (Datum), Auswahl als runde Pille.
import { e, heuteIso, plusTage, montag } from './ui.js';

export const ZEITBEREICHE = [['woche', 'Aktuelle Woche'], ['monat', 'Aktueller Monat'], ['bisende', 'Bis Schuljahresende'], ['schuljahr', 'Aktuelles Schuljahr'], ['bereich', 'Datumsbereich']];

/** Von/Bis eines Zeitbereichs (sj: Schuljahr mit von/bis). */
export function zeitbereichDaten(art, sj = {}) {
  const h = heuteIso();
  if (art === 'woche') return [montag(h), plusTage(montag(h), 6)];
  if (art === 'monat') { const d = new Date(`${h.slice(0, 7)}-01T12:00`); d.setMonth(d.getMonth() + 1); d.setDate(0); return [`${h.slice(0, 8)}01`, `${h.slice(0, 8)}${String(d.getDate()).padStart(2, '0')}`]; }
  if (art === 'bisende') return [h, sj?.bis || h];
  if (art === 'schuljahr') return [sj?.von || h, sj?.bis || h];
  return null;
}

/** Welcher Zeitbereich passt zu von/bis (sonst „Datumsbereich“). */
export const zeitbereichErkennen = (von, bis, sj) => ZEITBEREICHE.find(([k]) => { const d = zeitbereichDaten(k, sj); return d && d[0] === von && d[1] === bis; })?.[0] ?? 'bereich';

/** „Zeitraum“ mit Von, Bis und Auswahl (data-von, data-bis, data-zeitbereich). */
export const zeitbereichFeld = (von, bis, sj, beschriftung = 'Zeitraum') => {
  const art = zeitbereichErkennen(von, bis, sj);
  return `<span class="sg-feld zb-feld"><span>${e(beschriftung)}</span>
    <label class="nl-pille u-sf-datum"><span class="visually-hidden">Von</span><input type="date" data-von value="${e(von)}" aria-label="Von"></label>
    <label class="nl-pille u-sf-datum"><span class="visually-hidden">Bis</span><input type="date" data-bis value="${e(bis)}" aria-label="Bis"></label>
    <select class="form-select" data-zeitbereich aria-label="Zeitbereich" style="width:auto;flex:0 0 auto">${ZEITBEREICHE.map(([k, t]) => `<option value="${k}" ${k === art ? 'selected' : ''}>${t}</option>`).join('')}</select></span>`;
};

/** Verhalten: Auswahl setzt die Daten, geänderte Daten → „Datumsbereich“; beiWahl(von, bis) nach jeder Änderung. */
export function zeitbereichBinden(wurzel, sj, beiWahl) {
  const v = wurzel.querySelector('[data-von]'), b = wurzel.querySelector('[data-bis]'), s = wurzel.querySelector('[data-zeitbereich]');
  if (!v || !b || !s) return;
  s.onchange = () => { const d = zeitbereichDaten(s.value, sj); if (d) { [v.value, b.value] = d; beiWahl(d[0], d[1]); } };
  [v, b].forEach(x => x.onchange = () => { s.value = zeitbereichErkennen(v.value, b.value, sj); if (v.value && b.value) beiWahl(v.value, b.value); });
}
