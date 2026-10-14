// Mitteilungen wie im Original: Liste links (Posteingang bzw. Gesendet), Lesebereich rechts, neue Mitteilung schreiben.
import { e, icon, holen, senden, api, toast, dialog, bestaetigen, leer, initialen } from '../ui.js';

function zeit(s) {
  const d = new Date(s.replace(' ', 'T'));
  if (isNaN(d)) return s;
  const heute = new Date();
  return d.toDateString() === heute.toDateString()
    ? d.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })
    : d.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: d.getFullYear() === heute.getFullYear() ? undefined : '2-digit' });
}

const kuerzel = name => initialen(String(name || '?').replace(/\(.*\)/, ''));

export async function zeigen(seite, kontext, [ordnerParam, idParam]) {
  const ordner = ordnerParam === 'gesendet' ? 'gesendet' : 'eingang';
  kontext.titel(ordner === 'gesendet' ? 'Gesendet' : 'Posteingang', `<button class="btn btn-primary" data-neu>${icon('plus')}Neue Mitteilung</button>`);
  document.querySelector('[data-neu]').onclick = () => schreiben(kontext);

  const liste = await holen(`/api/app/mitteilungen?ordner=${ordner}`);
  const ungelesen = liste.filter(m => ordner === 'eingang' && !m.gelesen).length;
  seite.innerHTML = `
    <div class="mt-raster">
      <section class="u-panel" data-listenspalte>
        <header><h2>${ordner === 'gesendet' ? 'Gesendete Mitteilungen' : 'Mitteilungen'}</h2>
          ${ungelesen ? `<span class="u-chip orange ms-auto">${ungelesen} ungelesen</span>` : `<span class="ms-auto small text-body-secondary">${liste.length}</span>`}</header>
        <div class="px-3 pt-3 pb-2"><input type="search" class="form-control form-control-sm" data-suche placeholder="Suchen" aria-label="Mitteilungen durchsuchen"></div>
        <div class="mt-liste">${liste.length ? liste.map((m, i) => `
          <button type="button" class="mt-eintrag ${ordner === 'eingang' && !m.gelesen ? 'neu' : ''}" data-i="${i}" data-text="${e((m.betreff + ' ' + m.absender + ' ' + m.text).toLowerCase())}">
            <span class="u-avatar">${e(kuerzel(ordner === 'gesendet' ? m.empfaenger[0] : m.absender))}</span>
            <span class="rumpf">
              <span class="kopf"><span>${ordner === 'gesendet' ? 'an ' + e(m.empfaenger.join(', ')) : e(m.absender)}</span><span class="text-nowrap">${e(zeit(m.erstellt))}</span></span>
              <span class="betreff d-block">${m.wichtig ? '<span class="u-chip rot me-1">wichtig</span>' : ''}${e(m.betreff)}</span>
              <span class="vorschau d-block">${e(m.text.slice(0, 120))}</span>
            </span></button>`).join('')
          : leer(ordner === 'gesendet' ? 'Noch keine Mitteilungen gesendet.' : 'Keine Mitteilungen.', ordner === 'gesendet' ? 'gesendet' : 'posteingang')}</div>
      </section>
      <section class="u-panel" data-lesespalte><div class="mt-lesen" data-lesen>${leer('Wählen Sie eine Mitteilung aus.', 'mitteilungen')}</div></section>
    </div>`;
  const lesen = seite.querySelector('[data-lesen]');
  const schmal = () => window.innerWidth < 992;
  const ansicht = lesemodus => {
    if (!schmal()) return;
    seite.querySelector('[data-listenspalte]').classList.toggle('d-none', lesemodus);
    seite.querySelector('[data-lesespalte]').classList.toggle('d-none', !lesemodus);
  };
  ansicht(false);
  seite.querySelector('[data-suche]').oninput = ev => {
    const f = ev.target.value.trim().toLowerCase();
    seite.querySelectorAll('[data-i]').forEach(b => b.classList.toggle('d-none', !!f && !b.dataset.text.includes(f)));
  };

  const oeffnen = async i => {
    const m = liste[i];
    seite.querySelectorAll('[data-i]').forEach(b => b.classList.toggle('aktiv', Number(b.dataset.i) === i));
    ansicht(true);
    lesen.innerHTML = `
      <div class="d-flex flex-wrap gap-2 mb-3">
        <button class="btn btn-sm btn-outline-secondary d-lg-none" data-zurueck>${icon('links')} Zurück</button>
        ${ordner === 'eingang' && m.absenderId ? `<button class="btn btn-sm btn-outline-secondary" data-antworten>${icon('senden')}Antworten</button>` : ''}
        <button class="btn btn-sm btn-outline-danger ms-auto" data-loeschen>${icon('loeschen')}Löschen</button>
      </div>
      <h2>${m.wichtig ? '<span class="u-chip rot me-2 align-middle">wichtig</span>' : ''}${e(m.betreff)}</h2>
      <div class="mt-meta"><span class="u-avatar">${e(kuerzel(m.absender))}</span>
        <div><b>${e(m.absender)}</b><div>an ${e(m.empfaenger.join(', '))}</div></div>
        <span class="ms-auto text-nowrap">${e(new Date(m.erstellt.replace(' ', 'T')).toLocaleString('de-DE', { dateStyle: 'medium', timeStyle: 'short' }))}</span></div>
      <div class="mt-text">${e(m.text)}</div>`;
    lesen.querySelector('[data-zurueck]').onclick = () => ansicht(false);
    lesen.querySelector('[data-antworten]')?.addEventListener('click', () =>
      schreiben(kontext, { an: 'benutzer:' + m.absenderId, betreff: m.betreff.startsWith('AW:') ? m.betreff : 'AW: ' + m.betreff, zitat: m }));
    lesen.querySelector('[data-loeschen]').onclick = async () => {
      if (!await bestaetigen('Mitteilung löschen', `„${m.betreff}“ wirklich löschen?`)) return;
      await api(`/api/app/mitteilungen/${m.id}`, { methode: 'DELETE' });
      toast('Mitteilung gelöscht.');
      kontext.navigieren(ordner === 'gesendet' ? '#/mitteilungen/gesendet' : '#/mitteilungen');
    };
    if (ordner === 'eingang' && !m.gelesen) {
      try {
        await senden(`/api/app/mitteilungen/${m.id}/gelesen`, {});
        m.gelesen = new Date().toISOString();
        seite.querySelector(`[data-i="${i}"]`)?.classList.remove('neu');
        await kontext.sitzungNeu();
      } catch { /* nicht kritisch */ }
    }
  };
  seite.querySelectorAll('[data-i]').forEach(b => b.onclick = () => oeffnen(Number(b.dataset.i)));
  const gewuenscht = liste.findIndex(m => String(m.id) === idParam);
  if (gewuenscht >= 0) oeffnen(gewuenscht);
  else if (liste.length && !schmal()) oeffnen(0);
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
        <div class="border rounded overflow-auto px-3 py-2" style="max-height:220px">${gruppen.map(g => `<div class="small text-body-secondary fw-semibold mt-2 mb-1">${e(g)}</div>` + empfaenger.filter(x => x.art === g).map(x => `
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
