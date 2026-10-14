// Mitteilungen: Posteingang, Gesendet, neue Mitteilung schreiben.
import { e, icon, holen, senden, api, toast, dialog, bestaetigen, leer } from '../ui.js';

function zeit(s) {
  const d = new Date(s.replace(' ', 'T'));
  if (isNaN(d)) return s;
  const heute = new Date();
  return d.toDateString() === heute.toDateString()
    ? d.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })
    : d.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: d.getFullYear() === heute.getFullYear() ? undefined : '2-digit' });
}

export async function zeigen(seite, kontext, [ordnerParam]) {
  const ordner = ordnerParam === 'gesendet' ? 'gesendet' : 'eingang';
  kontext.titel('Mitteilungen', `<button class="btn btn-primary btn-sm" data-neu>${icon('plus', 'me-1')}Neue Mitteilung</button>`);
  document.querySelector('[data-neu]').onclick = () => schreiben(kontext);

  const liste = await holen(`/api/app/mitteilungen?ordner=${ordner}`);
  seite.innerHTML = `<div class="row g-3">
    <div class="col-lg-4" data-listenspalte>
      <div class="card">
        <div class="card-header p-0 border-bottom-0"><ul class="nav nav-tabs card-header-tabs m-0 px-2 pt-2">
          <li class="nav-item"><a class="nav-link ${ordner === 'eingang' ? 'active' : ''}" href="#/mitteilungen">Posteingang</a></li>
          <li class="nav-item"><a class="nav-link ${ordner === 'gesendet' ? 'active' : ''}" href="#/mitteilungen/gesendet">Gesendet</a></li>
        </ul></div>
        <div class="list-group list-group-flush" style="max-height:70vh;overflow-y:auto">${liste.length ? liste.map((m, i) => `
          <button type="button" class="list-group-item list-group-item-action py-2" data-i="${i}">
            <div class="d-flex small"><span class="text-truncate ${ordner === 'eingang' && !m.gelesen ? 'fw-semibold' : 'text-body-secondary'}">${ordner === 'gesendet' ? 'an ' + e(m.empfaenger.join(', ')) : e(m.absender)}</span>
              <span class="ms-auto ps-2 text-body-secondary text-nowrap">${e(zeit(m.erstellt))}</span></div>
            <div class="text-truncate ${ordner === 'eingang' && !m.gelesen ? 'fw-semibold' : ''}">${m.wichtig ? '<span class="badge text-bg-danger me-1">wichtig</span>' : ''}${e(m.betreff)}</div>
            <div class="small text-body-secondary text-truncate">${e(m.text.slice(0, 120))}</div></button>`).join('')
          : `<div class="list-group-item">${leer('Keine Mitteilungen.')}</div>`}</div>
      </div>
    </div>
    <div class="col-lg-8" data-lesespalte><div class="card"><div class="card-body" data-lesen>${leer('Wählen Sie eine Mitteilung aus.')}</div></div></div>
  </div>`;
  const lesen = seite.querySelector('[data-lesen]');
  const schmal = () => window.innerWidth < 992;
  const ansicht = lesemodus => {
    if (!schmal()) return;
    seite.querySelector('[data-listenspalte]').classList.toggle('d-none', lesemodus);
    seite.querySelector('[data-lesespalte]').classList.toggle('d-none', !lesemodus);
  };
  ansicht(false);

  const oeffnen = async i => {
    const m = liste[i];
    seite.querySelectorAll('[data-i]').forEach(b => b.classList.toggle('active', Number(b.dataset.i) === i));
    ansicht(true);
    lesen.innerHTML = `
      <div class="d-flex gap-2 mb-3">
        <button class="btn btn-sm btn-outline-secondary d-lg-none" data-zurueck>${icon('links')} Zurück</button>
        ${ordner === 'eingang' && m.absenderId ? `<button class="btn btn-sm btn-outline-secondary" data-antworten>${icon('senden', 'me-1')}Antworten</button>` : ''}
        <button class="btn btn-sm btn-outline-danger ms-auto" data-loeschen>${icon('loeschen', 'me-1')}Löschen</button>
      </div>
      <h2 class="h5">${m.wichtig ? '<span class="badge text-bg-danger me-2 align-middle">wichtig</span>' : ''}${e(m.betreff)}</h2>
      <div class="small text-body-secondary mb-3 pb-3 border-bottom"><span class="text-body fw-semibold">${e(m.absender)}</span> an ${e(m.empfaenger.join(', '))} · ${e(new Date(m.erstellt.replace(' ', 'T')).toLocaleString('de-DE'))}</div>
      <div style="white-space:pre-wrap">${e(m.text)}</div>`;
    lesen.querySelector('[data-zurueck]').onclick = () => ansicht(false);
    lesen.querySelector('[data-antworten]')?.addEventListener('click', () =>
      schreiben(kontext, { an: 'benutzer:' + m.absenderId, betreff: m.betreff.startsWith('AW:') ? m.betreff : 'AW: ' + m.betreff, zitat: m }));
    lesen.querySelector('[data-loeschen]').onclick = async () => {
      if (!await bestaetigen('Mitteilung löschen', `„${m.betreff}“ wirklich löschen?`)) return;
      await api(`/api/app/mitteilungen/${m.id}`, { methode: 'DELETE' });
      toast('Mitteilung gelöscht.');
      kontext.navigieren(location.hash);
    };
    if (ordner === 'eingang' && !m.gelesen) {
      try {
        await senden(`/api/app/mitteilungen/${m.id}/gelesen`, {});
        m.gelesen = new Date().toISOString();
        seite.querySelectorAll(`[data-i="${i}"] .fw-semibold`).forEach(x => x.classList.remove('fw-semibold'));
        await kontext.sitzungNeu();
      } catch { /* nicht kritisch */ }
    }
  };
  seite.querySelectorAll('[data-i]').forEach(b => b.onclick = () => oeffnen(Number(b.dataset.i)));
  if (liste.length && !schmal()) oeffnen(0);
}

async function schreiben(kontext, { an, betreff = '', zitat } = {}) {
  const empfaenger = await holen('/api/app/mitteilungen/empfaenger');
  if (!empfaenger.length) { toast('Für Ihr Konto sind keine Empfänger verfügbar.', true); return; }
  const gruppen = [...new Set(empfaenger.map(x => x.art))];
  const text = zitat ? `\n\n----- ${zitat.absender} schrieb: -----\n${zitat.text}` : '';
  dialog({
    titel: 'Neue Mitteilung', breit: true,
    inhalt: `<div class="mb-3">
        <div class="d-flex align-items-center mb-2"><label class="form-label mb-0">Empfänger</label>
          <input type="search" class="form-control form-control-sm ms-auto" data-suche placeholder="Suchen" style="width:200px"></div>
        <div class="border rounded overflow-auto px-2 py-1" style="max-height:200px">${gruppen.map(g => `<div class="small text-body-secondary fw-semibold mt-2 mb-1">${e(g)}</div>` + empfaenger.filter(x => x.art === g).map(x => `
          <div class="form-check" data-name="${e(x.name.toLowerCase())}"><input class="form-check-input" type="checkbox" value="${e(x.id)}" id="mt-${e(x.id)}" ${x.id === an ? 'checked' : ''}>
            <label class="form-check-label" for="mt-${e(x.id)}">${e(x.name)}</label></div>`).join('')).join('')}</div>
      </div>
      <div class="mb-3"><label class="form-label">Betreff</label><input class="form-control" name="betreff" maxlength="200" value="${e(betreff)}" required></div>
      <div class="mb-3"><label class="form-label">Nachricht</label><textarea class="form-control" name="text" rows="8" required>${e(text)}</textarea></div>
      <div class="form-check"><input class="form-check-input" type="checkbox" name="wichtig" id="mt-wichtig"><label class="form-check-label" for="mt-wichtig">Als wichtig markieren</label></div>`,
    beimOeffnen: box => {
      box.querySelector('[data-suche]').oninput = ev => {
        const f = ev.target.value.trim().toLowerCase();
        box.querySelectorAll('[data-name]').forEach(l => l.classList.toggle('d-none', !!f && !l.dataset.name.includes(f)));
      };
    },
    knoepfe: [
      { text: 'Abbrechen' },
      {
        text: 'Senden', art: 'haupt', aktion: async box => {
          const ids = [...box.querySelectorAll('[data-name] input:checked')].map(x => x.value);
          if (!ids.length) { toast('Bitte mindestens einen Empfänger auswählen.', true); return false; }
          await senden('/api/app/mitteilungen', { empfaenger: ids, betreff: box.querySelector('[name=betreff]').value, text: box.querySelector('[name=text]').value, wichtig: box.querySelector('[name=wichtig]').checked });
          toast('Mitteilung gesendet.');
          if (location.hash.startsWith('#/mitteilungen')) kontext.navigieren('#/mitteilungen/gesendet');
        },
      },
    ],
  });
}
