// Neuigkeiten: was sich in UntPlanWeb geändert hat (Entwicklungsnews), neueste zuerst; gelesen wird je Gerät gemerkt.
import { e, holen } from '../ui.js';

export const GELESEN = 'untplan:neuigkeiten-gelesen';

export async function neueAnzahl() {
  try {
    const liste = await holen('/neuigkeiten.json');
    const gelesen = localStorage.getItem(GELESEN) ?? '';
    return liste.filter(n => `${n.datum}|${n.titel}` > gelesen).length;
  } catch { return 0; }
}

export async function zeigen(seite, kontext) {
  kontext.titel('Neuigkeiten');
  const liste = await holen('/neuigkeiten.json');
  let gelesen = '';
  try { gelesen = localStorage.getItem(GELESEN) ?? ''; } catch { /* privat */ }
  const datum = iso => new Date(iso + 'T12:00').toLocaleDateString('de-DE', { day: '2-digit', month: 'long', year: 'numeric' });
  seite.innerHTML = `<div class="nw-liste">${liste.map(n => `
    <article class="nw-eintrag ${`${n.datum}|${n.titel}` > gelesen ? 'neu' : ''}">
      <div class="nw-datum">${e(datum(n.datum))}${`${n.datum}|${n.titel}` > gelesen ? '<span class="nw-neu">Neu</span>' : ''}</div>
      <h3>${e(n.titel)}</h3>
      <ul>${n.punkte.map(p => `<li>${e(p)}</li>`).join('')}</ul>
    </article>`).join('')}</div>`;
  const neueste = liste.map(n => `${n.datum}|${n.titel}`).sort().at(-1) ?? '';
  try { localStorage.setItem(GELESEN, neueste); } catch { /* privat */ }
  document.querySelector('[data-neuigkeiten] .nw-zahl')?.remove();
}
