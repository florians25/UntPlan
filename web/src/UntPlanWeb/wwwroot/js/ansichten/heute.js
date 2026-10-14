// Übersicht: der nächste Unterrichtstag mit Stunden, Nachrichten, Terminen und Meldungen.
import { e, holen, datumLang, datumKurz, heuteIso, meldungText, statusMarker, leer } from '../ui.js';
import { tagesListe, kartenBinden } from '../plangitter.js';

export async function zeigen(seite, kontext) {
  const ich = kontext.ich;
  kontext.titel('Übersicht');
  const d = await holen('/api/app/heute');
  if (!d.paket) {
    seite.innerHTML = `<div class="card"><div class="card-body">
      <h2 class="h6">Noch keine Daten vorhanden</h2>
      <p class="text-body-secondary mb-0">Sobald die Schule die Daten aus UntPlan überträgt, erscheinen hier Stundenplan und Vertretungen.</p>
      ${ich.admin ? '<a class="btn btn-primary btn-sm mt-3" href="#/admin/sync">Synchronisation einrichten</a>' : ''}</div></div>`;
    return;
  }
  const planLink = d.element ? `#/plan/${d.typ}/${encodeURIComponent(d.element)}/${d.datum}` : '#/plan';
  const tagText = d.datum === heuteIso() ? 'Heute' : 'Nächster Schultag';

  seite.innerHTML = `
    ${ich.kollegium && d.offeneStunden ? `<div class="alert alert-warning py-2 small d-flex align-items-center">
      ${d.offeneStunden} ${d.offeneStunden === 1 ? 'Stunde' : 'Stunden'} der letzten zwei Wochen ohne Klassenbucheintrag.
      <a class="alert-link ms-auto" href="#/klassenbuch">Zum Klassenbuch</a></div>` : ''}
    <div class="row g-3">
      <div class="col-lg-7">
        <div class="card h-100">
          <div class="card-header d-flex align-items-center gap-2">
            <span>${tagText}: ${e(datumLang(d.datum))}${d.woche ? ` <span class="text-body-secondary fw-normal">· ${e(d.woche)}-Woche</span>` : ''}</span>
            ${d.aenderungen ? `<span class="badge text-bg-success">${d.aenderungen} ${d.aenderungen === 1 ? 'Änderung' : 'Änderungen'}</span>` : ''}
            <a class="btn btn-sm btn-outline-secondary ms-auto" href="${planLink}">Wochenplan</a>
          </div>
          <div class="card-body" data-tag>
            ${d.hinweise.map(h => `<div class="alert alert-warning py-2 small mb-2">${e(h)}</div>`).join('')}
            ${!d.element ? leer('Ihrem Konto ist keine Lehrkraft oder Klasse zugeordnet.')
              : d.karten.length || d.aufsichten.length || d.ereignisse.length ? tagesListe(d.karten, d.aufsichten, d.typ, d.ereignisse) : leer('Kein Unterricht an diesem Tag.')}
          </div>
        </div>
      </div>
      <div class="col-lg-5 d-flex flex-column gap-3">
        <div class="card">
          <div class="card-header">Nachrichten</div>
          ${d.nachrichten.length ? `<ul class="list-group list-group-flush">${d.nachrichten.map(n => `<li class="list-group-item small">${e(n)}</li>`).join('')}</ul>` : `<div class="card-body py-0">${leer('Keine Nachrichten.')}</div>`}
        </div>
        <div class="card">
          <div class="card-header d-flex align-items-center">Mitteilungen
            ${d.ungelesen ? `<span class="badge rounded-pill text-bg-primary ms-2">${d.ungelesen} neu</span>` : ''}
            <a class="ms-auto small fw-normal" href="#/mitteilungen">Öffnen</a></div>
          <div class="card-body py-2 small text-body-secondary">${d.ungelesen ? `Sie haben ${d.ungelesen} ungelesene ${d.ungelesen === 1 ? 'Mitteilung' : 'Mitteilungen'}.` : 'Keine ungelesenen Mitteilungen.'}</div>
        </div>
        ${d.termine.length ? `<div class="card"><div class="card-header">Termine</div>
          <div class="table-responsive"><table class="table table-sm table-card align-middle"><tbody>${d.termine.map(t => `<tr>
            <td class="text-nowrap" style="width:1%"><b>${datumKurz(t.von)}</b>${t.bis && t.bis !== t.von ? '–' + datumKurz(t.bis) : ''}${t.stundeVon ? `<div class="small text-body-secondary">${t.stundeVon}.–${t.stundeBis}. Std.</div>` : ''}</td>
            <td>${e(t.titel || t.art)}<div class="small text-body-secondary">${e(t.art)}${t.klassen?.length ? ' · ' + e(t.klassen.join(', ')) : ''}</div></td></tr>`).join('')}</tbody></table></div></div>` : ''}
        ${!ich.kollegium ? `<div class="card"><div class="card-header">Hausaufgaben</div>
          ${d.hausaufgaben.length ? `<div class="table-responsive"><table class="table table-sm table-card"><tbody>${d.hausaufgaben.map(h => `<tr>
            <td class="text-nowrap" style="width:1%"><b>${e(h.fach)}</b></td><td>${e(h.hausaufgabe)}${h.faellig ? `<div class="small text-body-secondary">bis ${datumKurz(h.faellig)}</div>` : ''}</td></tr>`).join('')}</tbody></table></div>`
            : `<div class="card-body py-0">${leer('Keine offenen Hausaufgaben.')}</div>`}</div>` : ''}
        ${ich.kollegium ? `<div class="card"><div class="card-header d-flex align-items-center">Meine Meldungen<a class="ms-auto small fw-normal" href="#/melden">Neue Meldung</a></div>
          ${d.meldungen.length ? `<ul class="list-group list-group-flush">${d.meldungen.map(m => `<li class="list-group-item small d-flex gap-2 align-items-center"><span>${e(meldungText(m))}</span><span class="ms-auto">${statusMarker(m.status)}</span></li>`).join('')}</ul>`
            : `<div class="card-body py-0">${leer('Keine Meldungen.')}</div>`}</div>` : ''}
      </div>
    </div>`;
  kartenBinden(seite.querySelector('[data-tag]'), d.karten, kontext);
}
