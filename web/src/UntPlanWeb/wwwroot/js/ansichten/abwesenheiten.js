// Abwesenheiten: Schülerabwesenheiten (Liste mit Filter, Entschuldigen durch die Klassenleitung) und eigene Abwesenheitsmeldungen der Lehrkraft.
import { e, icon, holen, senden, toast, leer, filterFeld, datumKachel, stundenText, heuteIso, plusTage, meldungText, statusMarker, herunterladen } from '../ui.js';

const STATUS = { offen: '<span class="u-chip gelb">offen</span>', entschuldigt: '<span class="u-chip gruen">entschuldigt</span>', unentschuldigt: '<span class="u-chip rot">unentschuldigt</span>' };

export async function zeigen(seite, kontext, [reiterParam, vonParam, bisParam, klasseParam, statusParam]) {
  const ich = kontext.ich;
  const el = await kontext.elemente();
  const reiter = reiterParam === 'meine' && ich.element ? 'meine' : 'schueler';
  const iso = /^\d{4}-\d{2}-\d{2}$/;
  const von = iso.test(vonParam ?? '') ? vonParam : plusTage(heuteIso(), -14);
  const bis = iso.test(bisParam ?? '') ? bisParam : heuteIso();
  const klasse = klasseParam && klasseParam !== '-' ? klasseParam : '';
  const status = ['offen', 'entschuldigt', 'unentschuldigt'].includes(statusParam) ? statusParam : '';
  const ziel = (r, v = von, b = bis, k = klasse, s = status) => `#/abwesenheiten/${r}/${v}/${b}/${encodeURIComponent(k || '-')}/${s || '-'}`;
  kontext.titel('Abwesenheiten', reiter === 'meine' ? `<a class="btn btn-primary" href="#/melden/abwesenheit">${icon('plus')}Abwesenheit melden</a>` : `<button class="btn btn-outline-secondary" data-csv>${icon('csv')}CSV</button>`);
  seite.innerHTML = `
    ${ich.element ? `<nav class="u-reiter mb-3"><a href="${ziel('schueler')}" class="${reiter === 'schueler' ? 'aktiv' : ''}">Schüler*innen</a><a href="${ziel('meine')}" class="${reiter === 'meine' ? 'aktiv' : ''}">Meine Abwesenheiten</a></nav>` : ''}
    <div data-inhalt></div>`;
  const inhalt = seite.querySelector('[data-inhalt]');

  if (reiter === 'meine') {
    const liste = (await holen('/api/app/meldungen')).filter(m => m.typ === 'absenz');
    inhalt.innerHTML = `<div class="card">${liste.length ? `<ul class="u-liste">${liste.map(m => `<li>${datumKachel(m.von)}
        <div class="u-zeilentext"><div class="titel">${e(m.grund || 'Abwesenheit')}</div><div class="unter">${e(meldungText(m))}</div>${m.text ? `<div class="small mt-1">${e(m.text)}</div>` : ''}</div>
        ${statusMarker(m.status)}</li>`).join('')}</ul>` : leer('Sie haben noch keine Abwesenheit gemeldet.', 'abwesenheiten')}</div>`;
    return;
  }

  const q = `von=${von}&bis=${bis}${klasse ? `&klasse=${encodeURIComponent(klasse)}` : ''}${status ? `&status=${status}` : ''}`;
  const liste = await holen(`/api/app/abwesenheiten?${q}`);
  const verwalten = liste.some(z => z.verwalten);
  inhalt.innerHTML = `
    <div class="u-filter">
      ${filterFeld('Von', `<input type="date" class="form-control" data-von value="${von}">`)}
      ${filterFeld('Bis', `<input type="date" class="form-control" data-bis value="${bis}">`)}
      ${filterFeld('Klasse', `<select class="form-select" data-klasse><option value="">alle</option>${el.klassen.map(k => `<option ${k.kurz === klasse ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select>`)}
      ${filterFeld('Status', `<select class="form-select" data-status><option value="">alle</option>${['offen', 'entschuldigt', 'unentschuldigt'].map(s => `<option ${s === status ? 'selected' : ''}>${s}</option>`).join('')}</select>`)}
      ${verwalten ? `<div class="u-rechts"><button class="btn btn-sm btn-outline-success" data-setzen="entschuldigt">entschuldigen</button><button class="btn btn-sm btn-outline-danger" data-setzen="unentschuldigt">unentschuldigt</button><button class="btn btn-sm btn-outline-secondary" data-setzen="offen">offen</button></div>` : ''}
    </div>
    <div class="card">${liste.length ? `<div class="card-header d-flex align-items-center">${liste.length} ${liste.length === 1 ? 'Abwesenheit' : 'Abwesenheiten'}
        <span class="ms-auto d-flex gap-2">${['offen', 'entschuldigt', 'unentschuldigt'].map(s => { const n = liste.filter(z => z.status === s).length; return n ? STATUS[s].replace('</span>', ` ${n}</span>`) : ''; }).join('')}</span></div>
      <div class="table-responsive"><table class="table table-hover table-card align-middle"><thead><tr>${verwalten ? '<th><input type="checkbox" class="form-check-input" data-alle aria-label="Alle auswählen"></th>' : ''}<th>Schüler*in</th><th>Klasse</th><th>Datum</th><th>Stunden</th><th>Art</th><th>Fach</th><th>Status</th><th>Bemerkung</th><th>erfasst von</th></tr></thead><tbody>
      ${liste.map((z, i) => `<tr>${verwalten ? `<td>${z.verwalten ? `<input type="checkbox" class="form-check-input" data-i="${i}" aria-label="auswählen">` : ''}</td>` : ''}
        <td class="fw-semibold">${e(z.schueler)}</td><td>${e(z.klasse)}</td><td class="text-nowrap">${new Date(z.datum + 'T12:00').toLocaleDateString('de-DE', { weekday: 'short', day: '2-digit', month: '2-digit' })}</td>
        <td class="text-nowrap">${stundenText(z.stundeVon, z.stundeBis)}${z.beginn ? `<div class="small text-body-secondary">${e(z.beginn)}–${e(z.ende)}</div>` : ''}</td>
        <td>${z.art === 'verspaetet' ? `<span class="u-chip gelb">verspätet ${z.minuten} min</span>` : 'abwesend'}</td><td>${e(z.faecher.join(', '))}</td>
        <td>${STATUS[z.status] ?? e(z.status)}</td><td class="small">${e(z.bemerkung)}</td><td class="small text-body-secondary">${e(z.lehrer.join(', '))}</td></tr>`).join('')}
      </tbody></table></div>` : leer('Keine Abwesenheiten im Zeitraum.', 'abwesenheiten')}</div>`;
  const f = n => inhalt.querySelector(`[data-${n}]`)?.value ?? '';
  inhalt.querySelectorAll('.u-filter input, .u-filter select').forEach(x => x.onchange = () => kontext.navigieren(ziel('schueler', f('von') || von, f('bis') || bis, f('klasse'), f('status'))));
  inhalt.querySelector('[data-alle]')?.addEventListener('change', ev => inhalt.querySelectorAll('[data-i]').forEach(c => { c.checked = ev.target.checked; }));
  inhalt.querySelectorAll('[data-setzen]').forEach(b => b.onclick = async () => {
    const ids = [...inhalt.querySelectorAll('[data-i]:checked')].flatMap(c => liste[Number(c.dataset.i)].ids);
    if (!ids.length) return toast('Bitte zuerst Abwesenheiten auswählen.', true);
    try { await senden('/api/app/klassenbuch/fehlzeiten/status', { ids, status: b.dataset.setzen }); toast('Status geändert.'); kontext.navigieren(location.hash); }
    catch (err) { toast(err.message, true); }
  });
  document.querySelector('[data-csv]')?.addEventListener('click', () => herunterladen(`abwesenheiten-${von}-${bis}.csv`,
    ['Schüler*in;Klasse;Datum;Stunden;Art;Minuten;Fach;Status;Bemerkung;erfasst von', ...liste.map(z => [z.schueler, z.klasse, z.datum, stundenText(z.stundeVon, z.stundeBis), z.art, z.minuten, z.faecher.join(', '), z.status, z.bemerkung, z.lehrer.join(', ')]
      .map(x => `"${String(x ?? '').replace(/"/g, '""')}"`).join(';'))].join('\r\n')));
}
