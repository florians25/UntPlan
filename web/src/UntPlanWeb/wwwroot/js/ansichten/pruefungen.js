// Prüfungen: Klausuren aus UntPlan und in UntPlanWeb vorgemerkte Prüfungen mit Filter nach Zeitraum, Klasse und Lehrkraft.
import { e, icon, holen, leer, filterFeld, datumKachel, stundenText, heuteIso, plusTage, herunterladen } from '../ui.js';

export async function zeigen(seite, kontext, [vonParam, bisParam, klasseParam, lehrerParam]) {
  const ich = kontext.ich;
  const el = await kontext.elemente();
  const iso = /^\d{4}-\d{2}-\d{2}$/;
  const von = iso.test(vonParam ?? '') ? vonParam : heuteIso();
  const bis = iso.test(bisParam ?? '') ? bisParam : plusTage(heuteIso(), 90);
  const klasse = klasseParam && klasseParam !== '-' ? klasseParam : '';
  const lehrer = lehrerParam && lehrerParam !== '-' ? lehrerParam : (ich.kollegium && ich.element && !klasseParam ? ich.element : '');
  kontext.titel('Prüfungen', `${ich.kollegium ? `<a class="btn btn-primary" href="#/melden/klausur">${icon('plus')}Prüfung eintragen</a>` : ''}<button class="btn btn-outline-secondary" data-csv>${icon('csv')}CSV</button><button class="btn btn-outline-secondary" data-drucken>${icon('drucken')}Drucken</button>`);
  const ziel = (v, b, k, l) => `#/pruefungen/${v}/${b}/${encodeURIComponent(k || '-')}/${encodeURIComponent(l || '-')}`;
  seite.innerHTML = `
    <div class="u-filter nicht-drucken">
      ${filterFeld('Von', `<input type="date" class="form-control" data-von value="${von}">`)}
      ${filterFeld('Bis', `<input type="date" class="form-control" data-bis value="${bis}">`)}
      ${ich.kollegium ? filterFeld('Klasse', `<select class="form-select" data-klasse><option value="">alle</option>${el.klassen.map(k => `<option ${k.kurz === klasse ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select>`)
        + filterFeld('Lehrkraft', `<select class="form-select" data-lehrer><option value="">alle</option>${el.lehrer.map(l => `<option value="${e(l.kurz)}" ${l.kurz === lehrer ? 'selected' : ''}>${e(l.kurz)}</option>`).join('')}</select>`) : ''}
    </div>
    <div class="card" data-inhalt></div>`;
  const f = n => seite.querySelector(`[data-${n}]`)?.value ?? '';
  seite.querySelectorAll('.u-filter input, .u-filter select').forEach(x => x.onchange = () => kontext.navigieren(ziel(f('von') || von, f('bis') || bis, f('klasse'), f('lehrer'))));
  document.querySelector('[data-drucken]').onclick = () => window.print();
  const q = `von=${von}&bis=${bis}${klasse ? `&klasse=${encodeURIComponent(klasse)}` : ''}${lehrer ? `&lehrer=${encodeURIComponent(lehrer)}` : ''}`;
  const liste = await holen(`/api/app/pruefungen?${q}`);
  const inhalt = seite.querySelector('[data-inhalt]');
  inhalt.innerHTML = liste.length ? `<div class="card-header">${liste.length} ${liste.length === 1 ? 'Prüfung' : 'Prüfungen'}</div>
    <ul class="u-liste">${liste.map(p => `<li class="${p.datum < heuteIso() ? 'opacity-75' : ''}">${datumKachel(p.datum)}
      <div class="u-zeilentext"><div class="titel">${e(p.titel)}</div>
        <div class="unter">${stundenText(p.stundeVon, p.stundeBis)} Std.${p.beginn ? ` · ${e(p.beginn)}–${e(p.ende)}` : ''}</div>
        <div class="d-flex flex-wrap gap-1 mt-1">${p.klassen.map(k => `<span class="u-chip">${icon('klasse')}${e(k)}</span>`).join('')}${p.lehrer.map(l => `<span class="u-chip">${icon('lehrkraft')}${e(l)}</span>`).join('')}${p.raeume.map(r => `<span class="u-chip">${icon('raumplan')}${e(r)}</span>`).join('')}</div>
        ${p.text ? `<div class="small mt-1">${e(p.text)}</div>` : ''}</div>
      ${p.status === 'vorgemerkt' ? '<span class="u-chip">vorgemerkt</span>' : '<span class="u-chip gelb">Prüfung</span>'}</li>`).join('')}</ul>`
    : leer('Keine Prüfungen im gewählten Zeitraum.', 'pruefung');
  document.querySelector('[data-csv]').onclick = () => herunterladen(`pruefungen-${von}-${bis}.csv`,
    ['Datum;Stunden;Beginn;Ende;Titel;Klassen;Lehrkräfte;Räume;Status', ...liste.map(p => [p.datum, stundenText(p.stundeVon, p.stundeBis), p.beginn, p.ende, p.titel, p.klassen.join(', '), p.lehrer.join(', '), p.raeume.join(', '), p.status]
      .map(x => `"${String(x ?? '').replace(/"/g, '""')}"`).join(';'))].join('\r\n'));
}
