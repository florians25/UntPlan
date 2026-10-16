// Vertretungsplan als Tabelle (Schüler- bzw. Lehrerversion je nach Konto).
import { e, icon, holen, heuteIso, plusTage, datumLang, lade } from '../ui.js';

const CHIP = { entfall: 'rot', offen: 'rot', klausur: 'gelb', geaendert: 'gruen' };
const alt = w => w ? `<span class="alt">${e(w)}</span>` : '';
/** Wert wie auf der Karte: alter Wert durchgestrichen, neuer als grüne Plakette; ohne Ersatz (z. B. Vertretung ohne Lehrkraft) der alte Wert durchgestrichen in der Plakette. */
/** Zwischen altem und neuem Wert: bei Lehrkräften „LAN | ILK“, bei Raum und Fach „B125 → A300“. */
const wert = (alter, neuer, status, ohneErsatz = false, trenn = '→') => status === 'entfall' ? `${alt(alter)}${e(neuer)}`
  : alter && !neuer ? (ohneErsatz ? `<span class="lbl-neu weg">${e(alter)}</span>` : alt(alter))
  : alter ? `${alt(alter)}<span class="v-trenn">${trenn}</span> <span class="lbl-neu">${e(neuer)}</span>` : e(neuer);

export function tagHtml(t, kollegium) {
  const infos = [
    t.abwesendeKlassen.length ? `<div><span class="text-body-secondary">Abwesende Klassen:</span> ${e(t.abwesendeKlassen.join(', '))}</div>` : '',
    kollegium && t.abwesendeLehrer.length ? `<div><span class="text-body-secondary">Abwesende Lehrkräfte:</span> ${e(t.abwesendeLehrer.join(', '))}</div>` : '',
    t.blockierteRaeume.length ? `<div><span class="text-body-secondary">Nicht verfügbare Räume:</span> ${e(t.blockierteRaeume.join(', '))}</div>` : '',
  ].filter(Boolean).join('');
  return `<section class="card mb-3">
    <div class="card-header d-flex align-items-center gap-2">${e(datumLang(t.datum))}${t.woche ? `<span class="text-body-secondary fw-normal">· ${e(t.woche)}-Woche</span>` : ''}
      <span class="ms-auto u-chip">${t.zeilen.length} ${t.zeilen.length === 1 ? 'Eintrag' : 'Einträge'}</span></div>
    ${t.nachrichten.length ? `<ul class="list-group list-group-flush">${t.nachrichten.map(n => `<li class="list-group-item small" style="border-left:3px solid var(--u-akzent)">${e(n)}</li>`).join('')}</ul>` : ''}
    ${t.veranstaltungen.length ? `<div class="card-body py-2 small border-bottom">${t.veranstaltungen.map(v => `<div class="tt-ereignis veranstaltung d-inline-block me-2 mb-1" style="line-height:24px">${e(v)}</div>`).join('')}</div>` : ''}
    ${infos ? `<div class="card-body py-2 small border-bottom">${infos}</div>` : ''}
    ${t.frei ? `<div class="card-body text-body-secondary">${e(t.freiGrund || 'Unterrichtsfrei')}</div>`
      : !t.zeilen.length ? '<div class="card-body text-body-secondary small">Keine Vertretungen.</div>'
      : `<div class="table-responsive"><table class="table table-sm table-hover table-card align-middle"><thead><tr>
          <th>Klasse(n)</th><th>Stunde</th><th>Fach</th><th>${kollegium ? 'Vertretung' : 'Lehrkraft'}</th><th>Raum</th><th>Art</th><th>Text</th></tr></thead><tbody>
          ${t.zeilen.map(z => `<tr class="vt-zeile ${e(z.status)}" data-klassen="${e(z.klassen.toLowerCase())}">
            <td class="fw-semibold">${e(z.klassen)}</td><td>${e(z.stunde)}</td>
            <td>${wert(z.fachAlt, z.fach, z.status)}</td>
            <td class="vt-lehrer">${z.status === 'offen' ? `${alt(z.lehrerAlt)}???` : wert(z.lehrerAlt, z.lehrer, z.status, z.ohneLehrer, '|')}</td>
            <td>${wert(z.raumAlt, z.raum, z.status)}</td>
            <td><span class="u-chip ${CHIP[z.status] ?? ''}">${e(z.art)}</span></td>
            <td class="small">${e(z.text)}${z.vertrVon ? `<div class="text-body-secondary">Vertr. von ${e(z.vertrVon)}</div>` : ''}${z.nach ? `<div class="text-body-secondary">verlegt nach ${e(z.nach)}</div>` : ''}</td></tr>`).join('')}
        </tbody></table></div>`}
  </section>`;
}

export async function zeigen(seite, kontext, [datumParam]) {
  const kollegium = kontext.ich.kollegium;
  const datum = /^\d{4}-\d{2}-\d{2}$/.test(datumParam ?? '') ? datumParam : heuteIso();
  kontext.titel('Vertretungsplan', `<button class="btn btn-outline-secondary" data-drucken>${icon('drucken')}Drucken</button>`);
  document.querySelector('[data-drucken]').onclick = () => window.print();
  let tage = 2;
  try { tage = Number(localStorage.getItem('untplan:vt-tage') ?? 2) || 2; } catch { /* privat */ }

  seite.innerHTML = `<div class="d-flex flex-wrap align-items-center gap-2 mb-3 nicht-drucken">
      ${kollegium ? '<input type="search" class="form-control" data-filter placeholder="Klasse filtern" style="width:180px" aria-label="Klasse filtern">' : ''}
      <div class="ms-md-auto d-flex flex-wrap gap-2">
        <div class="btn-group" role="group">${[1, 2, 5].map(n => `<button type="button" data-anzahl="${n}" class="btn btn-outline-secondary ${n === tage ? 'active' : ''}">${n === 1 ? '1 Tag' : n + ' Tage'}</button>`).join('')}</div>
        <div class="btn-group" role="group">
          <button type="button" class="btn btn-outline-secondary" data-tage="-1" title="Zurück">${icon('links')}</button>
          <button type="button" class="btn btn-outline-secondary" data-tage="0">Heute</button>
          <button type="button" class="btn btn-outline-secondary" data-tage="1" title="Weiter">${icon('rechts')}</button>
        </div>
        <input type="date" class="form-control" style="width:auto" value="${datum}" data-datum aria-label="Datum">
      </div>
    </div><div data-inhalt>${lade()}</div>`;
  const ziel = d => `#/vertretungen/${d}`;
  seite.querySelectorAll('[data-tage]').forEach(b => b.onclick = () => {
    const n = Number(b.dataset.tage);
    kontext.navigieren(ziel(n === 0 ? heuteIso() : plusTage(datum, n)));
  });
  seite.querySelector('[data-datum]').onchange = ev => { if (ev.target.value) kontext.navigieren(ziel(ev.target.value)); };
  seite.querySelectorAll('[data-anzahl]').forEach(b => b.onclick = () => {
    try { localStorage.setItem('untplan:vt-tage', b.dataset.anzahl); } catch { /* privat */ }
    kontext.navigieren(ziel(datum));
  });

  const istAktuell = kontext.aktuell();
  const r = await holen(`/api/app/vertretungen?datum=${datum}&tage=${tage}`);
  if (!istAktuell()) return;
  const inhalt = seite.querySelector('[data-inhalt]');
  inhalt.innerHTML = r.tage.length ? r.tage.map(t => tagHtml(t, kollegium)).join('') : '<div class="alert alert-secondary">Keine Unterrichtstage im gewählten Zeitraum.</div>';
  seite.querySelector('[data-filter]')?.addEventListener('input', ev => {
    const f = ev.target.value.trim().toLowerCase();
    inhalt.querySelectorAll('.vt-zeile').forEach(z => z.classList.toggle('d-none', !!f && !z.dataset.klassen.split(', ').some(k => k.startsWith(f))));
  });
}
