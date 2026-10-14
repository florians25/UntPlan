// Tagesübersicht für das Kollegium: alle Klassen, Lehrkräfte oder Räume eines Tages.
import { e, icon, holen, heuteIso, plusTage, datumLang, lade } from '../ui.js';
import { tagesGitter, legende, kartenBinden } from '../plangitter.js';

const TYPEN = { klasse: 'Klassen', lehrer: 'Lehrkräfte', raum: 'Räume' };

export async function zeigen(seite, kontext, [typParam, datumParam]) {
  const typ = TYPEN[typParam] ? typParam : 'klasse';
  const datum = /^\d{4}-\d{2}-\d{2}$/.test(datumParam ?? '') ? datumParam : heuteIso();
  const ziel = (t, d) => `#/tag/${t}/${d}`;
  kontext.titel('Tagesübersicht', `<button class="btn btn-outline-secondary btn-sm" data-drucken>${icon('drucken', 'me-1')}Drucken</button>`);
  document.querySelector('[data-drucken]').onclick = () => window.print();

  seite.innerHTML = `
    <div class="d-flex flex-wrap align-items-center gap-2 mb-3 nicht-drucken">
      <div class="btn-group" role="group">${Object.entries(TYPEN).map(([t, n]) => `<button type="button" data-typ="${t}" class="btn btn-outline-primary ${t === typ ? 'active' : ''}">${n}</button>`).join('')}</div>
      <input type="search" class="form-control" data-filter placeholder="Filtern" style="width:180px" aria-label="Filtern">
      <div class="ms-md-auto d-flex gap-2">
        <div class="btn-group" role="group">
          <button type="button" class="btn btn-outline-secondary" data-tage="-1" title="Vorheriger Tag">${icon('links')}</button>
          <button type="button" class="btn btn-outline-secondary" data-tage="0">Heute</button>
          <button type="button" class="btn btn-outline-secondary" data-tage="1" title="Nächster Tag">${icon('rechts')}</button>
        </div>
        <input type="date" class="form-control" style="width:auto" value="${datum}" data-datum aria-label="Datum">
      </div>
    </div>
    <div data-inhalt>${lade()}</div>`;
  seite.querySelectorAll('[data-typ]').forEach(b => b.onclick = () => kontext.navigieren(ziel(b.dataset.typ, datum)));
  seite.querySelectorAll('[data-tage]').forEach(b => b.onclick = () => {
    const n = Number(b.dataset.tage);
    let d = n === 0 ? heuteIso() : plusTage(datum, n);
    while (n !== 0 && [0, 6].includes(new Date(d + 'T12:00').getDay())) d = plusTage(d, n);
    kontext.navigieren(ziel(typ, d));
  });
  seite.querySelector('[data-datum]').onchange = ev => { if (ev.target.value) kontext.navigieren(ziel(typ, ev.target.value)); };

  const istAktuell = kontext.aktuell();
  const tag = await holen(`/api/app/tag?typ=${typ}&datum=${datum}`);
  if (!istAktuell()) return;
  const inhalt = seite.querySelector('[data-inhalt]');
  const zeichnen = filter => {
    const f = filter.trim().toLowerCase();
    const sicht = { ...tag, zeilen: f ? tag.zeilen.filter(z => (z.kurz + ' ' + z.name).toLowerCase().includes(f)) : tag.zeilen };
    const k = tag.kopf;
    const { html, karten } = tagesGitter(sicht);
    inhalt.innerHTML = `<div class="d-flex flex-wrap align-items-baseline gap-2 mb-2"><h2 class="h5 mb-0">${e(datumLang(tag.datum))}</h2>
        ${k.woche ? `<span class="text-body-secondary small">${e(k.woche)}-Woche</span>` : ''}
        <span class="badge ${k.tagesaktuell ? 'text-bg-success' : 'text-bg-secondary'}">${k.tagesaktuell ? 'tagesaktuell' : 'Regelplan'}</span></div>
      ${k.hinweise.map(h => `<div class="alert alert-warning py-2 small mb-2">${e(h)}</div>`).join('')}
      ${k.frei ? `<div class="alert alert-secondary">${e(k.freiGrund || 'Unterrichtsfrei')}</div>`
        : sicht.zeilen.length ? html : '<div class="alert alert-secondary">Keine Einträge.</div>'}
      ${legende(false)}`;
    kartenBinden(inhalt, karten, kontext);
  };
  zeichnen('');
  seite.querySelector('[data-filter]').oninput = ev => zeichnen(ev.target.value);
}
