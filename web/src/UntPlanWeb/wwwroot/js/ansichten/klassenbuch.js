// Klassenbuch: offene Einträge der Lehrkraft, Verlauf einer Klasse, Hausaufgaben (Klassenkonten).
import { e, holen, datumKurz, datumLang, heuteIso, plusTage, leer, lade } from '../ui.js';
import { karteDialog } from '../plangitter.js';
import { elementWahl } from '../elementwahl.js';

function verlaufTabelle(liste, kollegium) {
  if (!liste.length) return `<div class="card-body py-0">${leer('Keine Einträge im Zeitraum.')}</div>`;
  return `<div class="table-responsive"><table class="table table-sm table-hover table-card align-middle"><thead><tr><th>Datum</th><th>Std.</th><th>Fach</th>${kollegium ? '<th>Lehrkraft</th>' : ''}<th>Inhalt</th><th>Hausaufgabe</th>${kollegium ? '<th>Notiz</th>' : ''}</tr></thead><tbody>
    ${liste.map(x => `<tr><td class="text-nowrap">${e(datumKurz(x.datum))}</td><td>${x.stunde}.</td><td class="fw-semibold">${e(x.fach)}</td>${kollegium ? `<td>${e(x.lehrer)}</td>` : ''}
      <td>${e(x.thema)}</td><td>${e(x.hausaufgabe)}${x.faellig ? `<div class="small text-body-secondary">bis ${datumKurz(x.faellig)}</div>` : ''}</td>${kollegium ? `<td class="text-body-secondary small">${e(x.notiz)}</td>` : ''}</tr>`).join('')}
  </tbody></table></div>`;
}

export async function zeigen(seite, kontext, [klasseParam]) {
  const ich = kontext.ich;
  kontext.titel(ich.kollegium ? 'Klassenbuch' : 'Hausaufgaben');
  const el = await kontext.elemente();
  const klasse = ich.kollegium ? (klasseParam || el.klassen[0]?.kurz) : ich.element;

  if (!ich.kollegium) {
    if (!klasse) { seite.innerHTML = '<div class="alert alert-secondary">Ihrem Konto ist keine Klasse zugeordnet.</div>'; return; }
    const [ha, verlauf] = await Promise.all([
      holen(`/api/app/klassenbuch/hausaufgaben?klasse=${encodeURIComponent(klasse)}`),
      holen(`/api/app/klassenbuch/klasse?klasse=${encodeURIComponent(klasse)}`),
    ]);
    seite.innerHTML = `
      <div class="card mb-3"><div class="card-header">Offene Hausaufgaben · ${e(klasse)}</div>
        ${ha.length ? `<div class="table-responsive"><table class="table table-sm table-card align-middle"><thead><tr><th>Fällig</th><th>Fach</th><th>Aufgabe</th><th>Gestellt</th></tr></thead><tbody>
          ${ha.map(x => `<tr><td class="text-nowrap fw-semibold">${x.faellig ? e(datumLang(x.faellig)) : '–'}</td><td>${e(x.fach)}</td><td>${e(x.hausaufgabe)}</td><td class="text-body-secondary small">${e(datumKurz(x.datum))} · ${e(x.lehrer)}</td></tr>`).join('')}
          </tbody></table></div>` : `<div class="card-body py-0">${leer('Keine offenen Hausaufgaben.')}</div>`}</div>
      <div class="card"><div class="card-header">Unterrichtsinhalte</div>${verlaufTabelle(verlauf, false)}</div>`;
    return;
  }

  seite.innerHTML = `
    ${ich.element ? `<div class="card mb-3"><div class="card-header">Meine Stunden ohne Eintrag <span class="text-body-secondary fw-normal">(letzte 14 Tage)</span></div><div class="card-body" data-offen>${lade()}</div></div>` : ''}
    <div class="card">
      <div class="card-header d-flex flex-wrap align-items-center gap-2"><span class="me-2">Klassenbuch</span><span data-wahl></span>
        <div class="ms-auto d-flex align-items-center gap-2 fw-normal small">
          <input type="date" class="form-control form-control-sm" data-von value="${plusTage(heuteIso(), -28)}" aria-label="von"><span>bis</span>
          <input type="date" class="form-control form-control-sm" data-bis value="${plusTage(heuteIso(), 14)}" aria-label="bis"></div></div>
      <div data-verlauf>${lade()}</div>
    </div>`;
  if (klasse) seite.querySelector('[data-wahl]').replaceWith(elementWahl({ eintraege: el.klassen, wert: klasse, platzhalter: 'Klasse wählen', beiWahl: k => kontext.navigieren(`#/klassenbuch/${encodeURIComponent(k)}`) }));

  const verlaufLaden = async () => {
    const ziel = seite.querySelector('[data-verlauf]');
    if (!klasse) { ziel.innerHTML = `<div class="card-body">${leer('Keine Klassen vorhanden.')}</div>`; return; }
    const von = seite.querySelector('[data-von]').value, bis = seite.querySelector('[data-bis]').value;
    ziel.innerHTML = verlaufTabelle(await holen(`/api/app/klassenbuch/klasse?klasse=${encodeURIComponent(klasse)}&von=${von}&bis=${bis}`), true);
  };
  const offenLaden = async () => {
    const ziel = seite.querySelector('[data-offen]');
    if (!ziel) return;
    const offen = await holen('/api/app/klassenbuch/offen');
    ziel.innerHTML = offen.length ? `<div class="d-flex flex-wrap gap-2">${offen.map((o, i) => `<button type="button" class="btn btn-sm btn-outline-secondary text-start" data-o="${i}">
        <span class="fw-semibold">${e(o.fach)}</span> ${e(o.klassen.join(', '))}<div class="small text-body-secondary">${e(datumKurz(o.datum))} · ${o.von}. Std.</div></button>`).join('')}</div>`
      : '<span class="small text-body-secondary">Alle Stunden sind eingetragen.</span>';
    ziel.querySelectorAll('[data-o]').forEach(b => b.onclick = () => {
      const o = offen[Number(b.dataset.o)];
      karteDialog({ datum: o.datum, von: o.von, bis: o.bis, beginn: '', ende: '', status: 'regulaer', fach: o.fach, fachName: '', klassen: o.klassen.map(k => ({ k })), lehrer: [{ k: ich.element }], raeume: [], art: '', text: '', unterricht: o.unterricht },
        { ...kontext, nachSpeichern: () => { offenLaden(); verlaufLaden(); } });
    });
  };
  seite.querySelector('[data-von]').onchange = verlaufLaden;
  seite.querySelector('[data-bis]').onchange = verlaufLaden;
  await Promise.all([verlaufLaden(), offenLaden()]);
}
