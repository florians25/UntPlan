// Tagesübersicht für das Kollegium: alle Klassen, Lehrkräfte oder Räume eines Tages.
import { e, icon, holen, heuteIso, plusTage, datumLang } from '../ui.js';
import { tagesGitter, legende, kartenBinden } from '../plangitter.js';

const TYPEN = { klasse: 'Klassen', lehrer: 'Lehrkräfte', raum: 'Räume' };

export async function zeigen(seite, kontext, [typParam, datumParam]) {
  const typ = TYPEN[typParam] ? typParam : 'klasse';
  const datum = /^\d{4}-\d{2}-\d{2}$/.test(datumParam ?? '') ? datumParam : heuteIso();
  const ziel = (t, d) => `#/tag/${t}/${d}`;
  kontext.titel('Tagesübersicht', `<button class="knopf flach rund" data-drucken title="Drucken">${icon('drucken')}</button>`);
  document.querySelector('[data-drucken]').onclick = () => window.print();

  seite.innerHTML = `
    <div class="plan-steuerung">
      <div class="segment">${Object.entries(TYPEN).map(([t, n]) => `<button data-typ="${t}" class="${t === typ ? 'aktiv' : ''}">${n}</button>`).join('')}</div>
      <input type="search" data-filter placeholder="Filtern …" style="width:180px" aria-label="Filtern">
      <span style="flex:1"></span>
      <div class="segment">
        <button data-tage="-1" title="Vorheriger Tag">${icon('links', 'icon-klein')}</button>
        <button data-tage="0">Heute</button>
        <button data-tage="1" title="Nächster Tag">${icon('rechts', 'icon-klein')}</button>
      </div>
      <input type="date" value="${datum}" data-datum aria-label="Datum" style="width:auto">
    </div>
    <div data-inhalt><div class="lade">Wird geladen …</div></div>`;
  seite.querySelectorAll('[data-typ]').forEach(b => b.onclick = () => kontext.navigieren(ziel(b.dataset.typ, datum)));
  seite.querySelectorAll('[data-tage]').forEach(b => b.onclick = () => {
    const n = Number(b.dataset.tage);
    let d = n === 0 ? heuteIso() : plusTage(datum, n);
    // Wochenenden überspringen
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
    inhalt.innerHTML = `<div class="plan-titel"><h2>${e(datumLang(tag.datum))}</h2>${k.woche ? `<span class="gedimmt">${e(k.woche)}-Woche</span>` : ''}
        ${k.tagesaktuell ? '<span class="marker gruen">tagesaktuell</span>' : '<span class="marker">Regelplan</span>'}</div>
      ${k.hinweise.map(h => `<div class="hinweis warnung" style="margin-bottom:10px">${icon('warnung')}<div>${e(h)}</div></div>`).join('')}
      ${k.frei ? `<div class="karte"><div class="leer">${icon('kalender')}<p><b>${e(k.freiGrund || 'Unterrichtsfrei')}</b></p></div></div>`
        : sicht.zeilen.length ? html : '<div class="karte"><div class="leer">Keine Einträge.</div></div>'}
      ${legende(false)}`;
    kartenBinden(inhalt, karten, kontext);
  };
  zeichnen('');
  seite.querySelector('[data-filter]').oninput = ev => zeichnen(ev.target.value);
}
