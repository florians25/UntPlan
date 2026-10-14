// Mitteilungen: Posteingang, Gesendet, neue Mitteilung schreiben.
import { e, icon, holen, senden, api, toast, dialog, bestaetigen, initialen } from '../ui.js';

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
  kontext.titel('Mitteilungen', `<button class="knopf haupt" data-neu>${icon('plus', 'icon-klein')} Neue Mitteilung</button>`);
  document.querySelector('[data-neu]').onclick = () => schreiben(kontext);

  const liste = await holen(`/api/app/mitteilungen?ordner=${ordner}`);
  seite.innerHTML = `<div class="mitteilungen">
    <div class="karte mt-liste">
      <div class="reiter" style="margin:0;padding:0 8px">
        <a href="#/mitteilungen" class="${ordner === 'eingang' ? 'aktiv' : ''}">Posteingang</a>
        <a href="#/mitteilungen/gesendet" class="${ordner === 'gesendet' ? 'aktiv' : ''}">Gesendet</a>
      </div>
      <div class="mt-eintraege">${liste.length ? liste.map((m, i) => `<button class="mt-eintrag ${ordner === 'eingang' && !m.gelesen ? 'ungelesen' : ''}" data-i="${i}">
          <small style="float:right">${e(zeit(m.erstellt))}</small>
          <small>${ordner === 'gesendet' ? 'an ' + e(m.empfaenger.join(', ')) : e(m.absender)}</small>
          <b>${m.wichtig ? '<span class="marker rot" style="margin-right:4px">wichtig</span>' : ''}${e(m.betreff)}</b>
          <small style="display:block;white-space:nowrap;overflow:hidden;text-overflow:ellipsis">${e(m.text.slice(0, 120))}</small></button>`).join('')
        : `<div class="leer">${icon('mitteilung')}<p>Keine Mitteilungen.</p></div>`}</div>
    </div>
    <div class="karte mt-lesen-karte"><div class="mt-lesen" data-lesen><div class="leer">${icon('mitteilung')}<p>Wählen Sie eine Mitteilung aus.</p></div></div></div>
  </div>`;
  const huelle = seite.querySelector('.mitteilungen');
  const lesen = seite.querySelector('[data-lesen]');

  const oeffnen = async i => {
    const m = liste[i];
    seite.querySelectorAll('.mt-eintrag').forEach(b => b.classList.toggle('aktiv', Number(b.dataset.i) === i));
    huelle.classList.add('lesen');
    lesen.innerHTML = `
      <div class="leiste" style="margin-bottom:8px">
        <button class="knopf flach klein" data-zurueck>${icon('links', 'icon-klein')} Zurück</button>
        <span class="abstand"></span>
        ${ordner === 'eingang' && m.absenderId ? `<button class="knopf klein" data-antworten>${icon('senden', 'icon-klein')} Antworten</button>` : ''}
        <button class="knopf klein gefahr" data-loeschen>${icon('loeschen', 'icon-klein')} Löschen</button>
      </div>
      <h2 style="margin:0 0 10px">${m.wichtig ? '<span class="marker rot">wichtig</span> ' : ''}${e(m.betreff)}</h2>
      <div style="display:flex;gap:10px;align-items:center">
        <span class="avatar">${e(initialen(m.absender))}</span>
        <div><b>${e(m.absender)}</b><br><small class="gedimmt">an ${e(m.empfaenger.join(', '))} · ${e(new Date(m.erstellt.replace(' ', 'T')).toLocaleString('de-DE'))}</small></div>
      </div>
      <div class="mt-text">${e(m.text)}</div>`;
    lesen.querySelector('[data-zurueck]').onclick = () => huelle.classList.remove('lesen');
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
        seite.querySelector(`.mt-eintrag[data-i="${i}"]`)?.classList.remove('ungelesen');
        await kontext.sitzungNeu();
      } catch { /* nicht kritisch */ }
    }
  };
  seite.querySelectorAll('.mt-eintrag').forEach(b => b.onclick = () => oeffnen(Number(b.dataset.i)));
  if (liste.length && window.innerWidth > 900) oeffnen(0);
}

async function schreiben(kontext, { an, betreff = '', zitat } = {}) {
  const empfaenger = await holen('/api/app/mitteilungen/empfaenger');
  if (!empfaenger.length) { toast('Für Ihr Konto sind keine Empfänger verfügbar.', true); return; }
  const gruppen = [...new Set(empfaenger.map(x => x.art))];
  const text = zitat ? `\n\n----- ${zitat.absender} schrieb: -----\n${zitat.text}` : '';
  dialog({
    titel: 'Neue Mitteilung', breit: true,
    inhalt: `<div class="formular">
      <div><div class="leiste" style="margin-bottom:6px"><b class="klein">Empfänger</b><span class="abstand"></span>
        <input type="search" data-suche placeholder="Empfänger suchen …" style="width:220px"></div>
        <div class="empfaengerwahl">${gruppen.map(g => empfaenger.filter(x => x.art === g).map(x => `
          <label data-name="${e(x.name.toLowerCase())}"><input type="checkbox" value="${e(x.id)}" ${x.id === an ? 'checked' : ''}> ${e(x.name)}<small>${e(g)}</small></label>`).join('')).join('')}</div></div>
      <label class="feld">Betreff<input name="betreff" maxlength="200" value="${e(betreff)}" required></label>
      <label class="feld">Nachricht<textarea name="text" rows="8" required>${e(text)}</textarea></label>
      <label class="check"><input type="checkbox" name="wichtig"> Als wichtig markieren</label>
    </div>`,
    beimOeffnen: box => {
      box.querySelector('[data-suche]').oninput = ev => {
        const f = ev.target.value.trim().toLowerCase();
        box.querySelectorAll('.empfaengerwahl label').forEach(l => l.classList.toggle('versteckt', !!f && !l.dataset.name.includes(f)));
      };
      if (an) box.querySelector('[name=text]').focus();
    },
    knoepfe: [
      { text: 'Abbrechen' },
      {
        text: `${icon('senden', 'icon-klein')} Senden`, art: 'haupt', aktion: async box => {
          const ids = [...box.querySelectorAll('.empfaengerwahl input:checked')].map(x => x.value);
          if (!ids.length) { toast('Bitte mindestens einen Empfänger auswählen.', true); return false; }
          await senden('/api/app/mitteilungen', { empfaenger: ids, betreff: box.querySelector('[name=betreff]').value, text: box.querySelector('[name=text]').value, wichtig: box.querySelector('[name=wichtig]').checked });
          toast('Mitteilung gesendet.');
          if (location.hash.startsWith('#/mitteilungen')) kontext.navigieren('#/mitteilungen/gesendet');
        },
      },
    ],
  });
}
