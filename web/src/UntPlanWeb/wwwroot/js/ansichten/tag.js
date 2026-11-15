// Tagesübersicht für das Kollegium (Tag - Klassen/Lehrkräfte/Räume) wie in der Vorlage: je Element eine Spalte mit derselben senkrechten
// Zeitachse und denselben Karten wie im Wochenplan; oben links schaltet ein runder Knopf leere Spalten aus bzw. ein („Leere Spalten anzeigen“).
import { e, icon, holen, heuteIso, plusTage, datumLang, lade } from '../ui.js';
import { wochenGitter, kartenBinden, sperreHtml } from '../plangitter.js';
import { kalenderBinden } from './plan.js';

const TYPEN = { klasse: 'Klassen', lehrer: 'Lehrkräfte', raum: 'Räume' };

export async function zeigen(seite, kontext, [typParam, datumParam]) {
  const typ = TYPEN[typParam] ? typParam : 'klasse';
  const datum = /^\d{4}-\d{2}-\d{2}$/.test(datumParam ?? '') ? datumParam : heuteIso();
  const ziel = (t, d) => `#/tag/${t}/${d}`;
  kontext.titel('');
  document.title = `Tag - ${TYPEN[typ]} – UntPlanWeb`;

  // Kopfzeile wie im Stundenplan: Filter links, Tag mittig mit Blättern, „Heute“ rechts
  seite.innerHTML = `
    <div class="planleiste nicht-drucken">
      <div class="links"><input type="search" class="form-control tag-filter" data-filter placeholder="${TYPEN[typ]} filtern" aria-label="Filtern"></div>
      <div class="mitte">
        <button type="button" class="rundknopf" data-tage="-1" title="Vorheriger Tag" aria-label="Vorheriger Tag">${icon('links')}</button>
        <span class="position-relative"><button type="button" class="zeitraum" data-datum title="Datum wählen" aria-haspopup="dialog">${e(datumLang(datum))}</button></span>
        <button type="button" class="rundknopf" data-tage="1" title="Nächster Tag" aria-label="Nächster Tag">${icon('rechts')}</button>
      </div>
      <div class="rechts"><button type="button" class="rundknopf" data-drucken title="Drucken" aria-label="Drucken">${icon('drucken')}</button><button type="button" class="heute-pill" data-tage="0">Heute</button></div>
    </div>
    <div data-inhalt>${lade()}</div>`;
  seite.querySelector('[data-drucken]').onclick = () => window.print();
  seite.querySelectorAll('[data-tage]').forEach(b => b.onclick = () => {
    const n = Number(b.dataset.tage);
    let d = n === 0 ? heuteIso() : plusTage(datum, n);
    while (n !== 0 && [0, 6].includes(new Date(d + 'T12:00').getDay())) d = plusTage(d, n);
    kontext.navigieren(ziel(typ, d));
  });
  kalenderBinden(seite.querySelector('[data-datum]'), datum, d => kontext.navigieren(ziel(typ, d)), 'tag');

  const istAktuell = kontext.aktuell();
  const tag = await holen(`/api/app/tag?typ=${typ}&datum=${datum}`);
  if (!istAktuell()) return;
  const inhalt = seite.querySelector('[data-inhalt]');
  const LEER = 'untplan:tag-leere-aus';
  let leerAus = false;
  try { leerAus = localStorage.getItem(LEER) === '1'; } catch { /* privat */ }
  let filterText = '';
  const zeichnen = () => {
    const f = filterText.trim().toLowerCase();
    const k = tag.kopf;
    let zeilen = f ? tag.zeilen.filter(z => (z.kurz + ' ' + z.name).toLowerCase().includes(f)) : tag.zeilen;
    if (leerAus) zeilen = zeilen.filter(z => z.karten.length);
    // je Element eine Spalte des Rasters (die Karten behalten ihr Datum für die Stundendetails)
    const plan = { typ, zeitraster: tag.zeitraster, aufsichten: [], abwesenheiten: [], karten: [], gesperrt: false,
      tage: zeilen.map(z => ({ datum, spalte: z.kurz, titel: z.kurz, name: z.name, hinweise: [], ereignisse: [], frei: false })) };
    zeilen.forEach(z => z.karten.forEach(x => plan.karten.push({ ...x, spalte: z.kurz })));
    const ecke = `<button type="button" class="tt-leer-knopf ${leerAus ? 'aus' : ''}" data-leer data-hover="${leerAus ? 'Leere Spalten anzeigen' : 'Leere Spalten ausblenden'}" data-hover-art="dunkel"
      aria-label="${leerAus ? 'Leere Spalten anzeigen' : 'Leere Spalten ausblenden'}" aria-pressed="${leerAus}"><i class="bi ${leerAus ? 'bi-arrows-expand-vertical' : 'bi-arrows-collapse-vertical'}" aria-hidden="true"></i></button>`;
    inhalt.innerHTML = `${k.hinweise.map(h => `<div class="alert alert-warning py-2 small mb-2">${e(h)}</div>`).join('')}
      ${tag.gesperrt ? `<div class="tt-gesperrt tag-gesperrt">${sperreHtml(tag.sperrhinweis)}</div>`
        : k.frei ? `<div class="alert alert-secondary">${e(k.freiGrund || 'Unterrichtsfrei')}</div>`
        : zeilen.length ? `<div class="tag-huelle" style="--spalten:${zeilen.length}">${wochenGitter(plan, { ecke })}</div>`
        : `<div class="alert alert-secondary">${leerAus && tag.zeilen.length ? 'Keine Einträge – leere Spalten sind ausgeblendet.' : 'Keine Einträge.'}</div>`}`;
    kartenBinden(inhalt, plan.karten, kontext, typ);
    inhalt.querySelector('[data-leer]')?.addEventListener('click', () => {
      leerAus = !leerAus;
      try { localStorage.setItem(LEER, leerAus ? '1' : '0'); } catch { /* privat */ }
      zeichnen();
    });
  };
  zeichnen();
  seite.querySelector('[data-filter]').oninput = ev => { filterText = ev.target.value; zeichnen(); };
}
