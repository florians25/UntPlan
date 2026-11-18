// Zeitraum wie im Original: zwei Datumsfelder und daneben die Auswahl „Aktuelle Woche“, „Aktueller Monat“, „Bis Schuljahresende“,
// „Aktuelles Schuljahr“ bzw. „Datumsbereich“ (frei gewählte Daten). Gemeinsam für Abwesenheiten, Klassendienste, Klassenbucheinträge,
// Berichte und Unterricht → Klasse. Bausteine: sg-feld (Beschriftung darüber), ‹ Von Bis › als Pillen, Auswahl als runde Pille (modulseiten.css).
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

/** Zeitraum um seine Länge verschieben (richtung −1 = zurück, +1 = vor): gleich viele Tage, Monate bleiben ganze Monate. */
export function zeitraumSchieben(von, bis, richtung) {
  const ganzerMonat = von.slice(8) === '01' && plusTage(bis, 1).slice(8) === '01' && von.slice(0, 7) === bis.slice(0, 7);
  if (ganzerMonat) {
    const d = new Date(`${von}T12:00`); d.setMonth(d.getMonth() + richtung);
    const v = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-01`;
    const e2 = new Date(d.getFullYear(), d.getMonth() + 1, 0);
    return [v, `${v.slice(0, 8)}${String(e2.getDate()).padStart(2, '0')}`];
  }
  const tage = Math.round((new Date(`${bis}T12:00`) - new Date(`${von}T12:00`)) / 864e5) + 1;
  return [plusTage(von, richtung * tage), plusTage(bis, richtung * tage)];
}

/** Pfeil ‹ bzw. › zum Verschieben eines Zeitraums (data-zb-schieben="-1" | "1"). */
export const zeitraumPfeil = richtung => `<button type="button" class="u-fl-pfeil" data-zb-schieben="${richtung}" aria-label="${richtung < 0 ? 'Zeitraum zurück' : 'Zeitraum vor'}" title="${richtung < 0 ? 'Zeitraum zurück' : 'Zeitraum vor'}"><i class="bi bi-chevron-${richtung < 0 ? 'left' : 'right'}" aria-hidden="true"></i></button>`;

/** „Zeitraum“ mit ‹ Von Bis › und Auswahl (data-von, data-bis, data-zeitbereich). */
export const zeitbereichFeld = (von, bis, sj, beschriftung = 'Zeitraum') => {
  const art = zeitbereichErkennen(von, bis, sj);
  return `<span class="sg-feld zb-feld"><span>${e(beschriftung)}</span><span class="zb-steuer">${zeitraumPfeil(-1)}
    <label class="nl-pille u-sf-datum"><span class="visually-hidden">Von</span><input type="date" data-von value="${e(von)}" aria-label="Von"></label>
    <label class="nl-pille u-sf-datum"><span class="visually-hidden">Bis</span><input type="date" data-bis value="${e(bis)}" aria-label="Bis"></label>${zeitraumPfeil(1)}
    <select class="form-select" data-zeitbereich aria-label="Zeitbereich">${ZEITBEREICHE.map(([k, t]) => `<option value="${k}" ${k === art ? 'selected' : ''}>${t}</option>`).join('')}</select></span></span>`;
};

/** Verhalten: Auswahl setzt die Daten, geänderte Daten → „Datumsbereich“, Pfeile verschieben den Zeitraum; beiWahl(von, bis) nach jeder Änderung. */
export function zeitbereichBinden(wurzel, sj, beiWahl) {
  const v = wurzel.querySelector('[data-von]'), b = wurzel.querySelector('[data-bis]'), s = wurzel.querySelector('[data-zeitbereich]');
  if (!v || !b || !s) return;
  s.onchange = () => { const d = zeitbereichDaten(s.value, sj); if (d) { [v.value, b.value] = d; beiWahl(d[0], d[1]); } };
  [v, b].forEach(x => x.onchange = () => { s.value = zeitbereichErkennen(v.value, b.value, sj); if (v.value && b.value) beiWahl(v.value, b.value); });
  pfeileBinden(wurzel, () => [v.value, b.value], (nv, nb) => { v.value = nv; b.value = nb; s.value = zeitbereichErkennen(nv, nb, sj); beiWahl(nv, nb); });
}

/** Pfeile ‹ › (zeitraumPfeil) in wurzel: holen() liefert [von, bis], setzen(von, bis) übernimmt den verschobenen Zeitraum. */
export function pfeileBinden(wurzel, holen, setzen) {
  wurzel.querySelectorAll('[data-zb-schieben]').forEach(k => k.onclick = () => {
    const [von, bis] = holen();
    if (!/^\d{4}-\d{2}-\d{2}$/.test(von ?? '') || !/^\d{4}-\d{2}-\d{2}$/.test(bis ?? '') || bis < von) return;
    setzen(...zeitraumSchieben(von, bis, Number(k.dataset.zbSchieben)));
  });
}
