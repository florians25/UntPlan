// Tagesübersicht für das Kollegium: alle Klassen, Lehrkräfte oder Räume eines Tages.
import { e, icon, holen, heuteIso, plusTage, datumLang, lade } from '../ui.js';
import { tagesGitter, kartenBinden } from '../plangitter.js';

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
      <div class="links"><input type="search" class="form-control" data-filter placeholder="${TYPEN[typ]} filtern" style="width:200px;border-radius:999px" aria-label="Filtern"></div>
      <div class="mitte">
        <button type="button" class="rundknopf" data-tage="-1" title="Vorheriger Tag" aria-label="Vorheriger Tag">${icon('links')}</button>
        <label class="zeitraum" title="Datum wählen">${e(datumLang(datum))}<input type="date" value="${datum}" data-datum aria-label="Datum"></label>
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
  const datumFeld = seite.querySelector('[data-datum]');
  datumFeld.onchange = ev => { if (ev.target.value) kontext.navigieren(ziel(typ, ev.target.value)); };
  datumFeld.onclick = () => { try { datumFeld.showPicker(); } catch { /* ältere Browser */ } };

  const istAktuell = kontext.aktuell();
  const tag = await holen(`/api/app/tag?typ=${typ}&datum=${datum}`);
  if (!istAktuell()) return;
  const inhalt = seite.querySelector('[data-inhalt]');
  const zeichnen = filter => {
    const f = filter.trim().toLowerCase();
    const sicht = { ...tag, zeilen: f ? tag.zeilen.filter(z => (z.kurz + ' ' + z.name).toLowerCase().includes(f)) : tag.zeilen };
    const k = tag.kopf;
    const { html, karten } = tagesGitter(sicht);
    inhalt.innerHTML = `<div class="d-flex flex-wrap align-items-baseline gap-2 mb-2"><h2 class="h6 fw-bold mb-0">Tag - ${e(TYPEN[typ])}</h2>
        <span class="badge ${k.tagesaktuell ? 'text-bg-success' : 'text-bg-secondary'}">${k.tagesaktuell ? 'tagesaktuell' : 'Regelplan'}</span></div>
      ${k.hinweise.map(h => `<div class="alert alert-warning py-2 small mb-2">${e(h)}</div>`).join('')}
      ${k.frei ? `<div class="alert alert-secondary">${e(k.freiGrund || 'Unterrichtsfrei')}</div>`
        : sicht.zeilen.length ? html : '<div class="alert alert-secondary">Keine Einträge.</div>'}`;
    kartenBinden(inhalt, karten, kontext, typ);
  };
  zeichnen('');
  seite.querySelector('[data-filter]').oninput = ev => zeichnen(ev.target.value);
}
