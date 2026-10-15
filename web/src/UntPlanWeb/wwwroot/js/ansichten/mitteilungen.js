// Mitteilungen wie im Original: Posteingang, Gesendet, Entwürfe und Listen; Liste links, Lesebereich rechts; Schreiben mit
// Empfängern (auch eigene Listen), Anhängen, Entwurf speichern; Antworten, Drucken, Löschen.
import { e, icon, holen, senden, api, toast, dialog, bestaetigen, leer, initialen } from '../ui.js';

function zeit(s) {
  const d = new Date(String(s).replace(' ', 'T'));
  if (isNaN(d)) return s;
  const heute = new Date();
  return d.toDateString() === heute.toDateString()
    ? d.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })
    : d.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: d.getFullYear() === heute.getFullYear() ? undefined : '2-digit' });
}

const kuerzel = name => initialen(String(name || '?').replace(/\(.*\)/, ''));
const groesse = n => n > 1048576 ? `${(n / 1048576).toFixed(1)} MB` : `${Math.max(1, Math.round(n / 1024))} KB`;
const TITEL = { eingang: 'Posteingang', gesendet: 'Gesendete Mitteilungen', entwuerfe: 'Entwürfe', listen: 'Listen' };

export async function zeigen(seite, kontext, [ordnerParam, idParam]) {
  const ordner = ['gesendet', 'entwuerfe', 'listen'].includes(ordnerParam) ? ordnerParam : 'eingang';
  if (ordner === 'listen') return listen(seite, kontext);
  kontext.titel(TITEL[ordner], `<button class="btn btn-primary" data-neu>${icon('plus')}Neue Mitteilung</button>`);
  document.querySelector('[data-neu]').onclick = () => schreiben(kontext);

  const liste = await holen(`/api/app/mitteilungen?ordner=${ordner}`);
  const ungelesen = liste.filter(m => ordner === 'eingang' && !m.gelesen).length;
  const kopfName = m => ordner === 'eingang' ? m.absender : ordner === 'entwuerfe' ? 'Entwurf' : 'an ' + m.empfaenger.join(', ');
  seite.innerHTML = `
    <div class="mt-raster">
      <section class="u-panel" data-listenspalte>
        <header><h2>${TITEL[ordner]}</h2>
          ${ungelesen ? `<span class="u-chip orange ms-auto">${ungelesen} ungelesen</span>` : `<span class="ms-auto small text-body-secondary">${liste.length}</span>`}</header>
        <div class="px-3 pt-3 pb-2"><input type="search" class="form-control form-control-sm" data-suche placeholder="Inhalt oder Person suchen" aria-label="Mitteilungen durchsuchen"></div>
        <div class="mt-liste">${liste.length ? liste.map((m, i) => `
          <button type="button" class="mt-eintrag ${ordner === 'eingang' && !m.gelesen ? 'neu' : ''}" data-i="${i}" data-text="${e((m.betreff + ' ' + m.absender + ' ' + m.text + ' ' + m.empfaenger.join(' ')).toLowerCase())}">
            <span class="u-avatar">${e(kuerzel(ordner === 'eingang' ? m.absender : m.empfaenger[0] ?? 'E'))}</span>
            <span class="rumpf">
              <span class="kopf"><span>${e(kopfName(m))}</span><span class="text-nowrap">${m.anhaenge?.length ? '<i class="bi bi-paperclip"></i> ' : ''}${e(zeit(m.erstellt))}</span></span>
              <span class="betreff d-block">${m.wichtig ? '<span class="u-chip rot me-1">wichtig</span>' : ''}${e(m.betreff || '(ohne Betreff)')}</span>
              <span class="vorschau d-block">${e(m.text.slice(0, 120))}</span>
            </span></button>`).join('')
          : leer(ordner === 'gesendet' ? 'Noch keine Mitteilungen gesendet.' : ordner === 'entwuerfe' ? 'Keine Entwürfe.' : 'Keine Mitteilungen.', ordner === 'gesendet' ? 'send' : 'inbox')}</div>
      </section>
      <section class="u-panel" data-lesespalte><div class="mt-lesen" data-lesen>${leer('Wählen Sie eine Mitteilung aus.', 'envelope')}</div></section>
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
    if (ordner === 'entwuerfe') { schreiben(kontext, { entwurf: m }); return; }
    seite.querySelectorAll('[data-i]').forEach(b => b.classList.toggle('aktiv', Number(b.dataset.i) === i));
    ansicht(true);
    lesen.innerHTML = `
      <div class="d-flex flex-wrap gap-2 mb-3 nicht-drucken">
        <button class="btn btn-sm btn-outline-secondary d-lg-none" data-zurueck>${icon('links')} Zurück</button>
        ${ordner === 'eingang' && m.absenderId ? `<button class="btn btn-sm btn-outline-secondary" data-antworten>${icon('senden')}Antworten</button>` : ''}
        <button class="btn btn-sm btn-outline-secondary" data-weiterleiten>Weiterleiten</button>
        <button class="btn btn-sm btn-outline-secondary" data-drucken>${icon('drucken')}Drucken</button>
        <button class="btn btn-sm btn-outline-danger ms-auto" data-loeschen>${icon('loeschen')}Löschen</button>
      </div>
      <h2>${m.wichtig ? '<span class="u-chip rot me-2 align-middle">wichtig</span>' : ''}${e(m.betreff)}</h2>
      <div class="mt-meta"><span class="u-avatar">${e(kuerzel(m.absender || 'Ich'))}</span>
        <div><b>${e(m.absender || 'Ich')}</b><div>an ${e(m.empfaenger.join(', '))}</div></div>
        <span class="ms-auto text-nowrap">${e(new Date(m.erstellt.replace(' ', 'T')).toLocaleString('de-DE', { dateStyle: 'medium', timeStyle: 'short' }))}</span></div>
      <div class="mt-text">${e(m.text)}</div>
      ${m.anhaenge?.length ? `<div class="mt-anhaenge">${m.anhaenge.map(a => `<a class="mt-anhang" href="/api/app/mitteilungen/anhang/${a.id}" download="${e(a.name)}"><i class="bi bi-paperclip"></i>${e(a.name)} <span class="text-body-secondary fw-normal">${groesse(a.groesse)}</span></a>`).join('')}</div>` : ''}`;
    lesen.querySelector('[data-zurueck]').onclick = () => ansicht(false);
    lesen.querySelector('[data-antworten]')?.addEventListener('click', () =>
      schreiben(kontext, { an: 'benutzer:' + m.absenderId, betreff: m.betreff.startsWith('AW:') ? m.betreff : 'AW: ' + m.betreff, zitat: m }));
    lesen.querySelector('[data-weiterleiten]').onclick = () => schreiben(kontext, { betreff: 'WG: ' + m.betreff, zitat: m });
    lesen.querySelector('[data-drucken]').onclick = () => { document.body.classList.add('mt-druck'); window.print(); setTimeout(() => document.body.classList.remove('mt-druck'), 500); };
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
  else if (liste.length && !schmal() && ordner !== 'entwuerfe') oeffnen(0);
}

/** Empfängerauswahl (Gruppen, Klassen, Listen, Benutzer) mit Suche. */
function empfaengerHtml(empfaenger, gewaehlt) {
  const gruppen = [...new Set(empfaenger.map(x => x.art))];
  const NAMEN = { gruppe: 'Gruppen', klasse: 'Klassen', liste: 'Meine Listen' };
  return `<div class="d-flex align-items-center mb-2"><label class="form-label mb-0">Empfänger*innen</label>
      <input type="search" class="form-control form-control-sm ms-auto" data-suche placeholder="Suchen" style="width:200px"></div>
    <div class="border rounded overflow-auto px-3 py-2" style="max-height:220px">${gruppen.map(g => `<div class="small text-body-secondary fw-bold mt-2 mb-1">${e(NAMEN[g] ?? g)}</div>` + empfaenger.filter(x => x.art === g).map(x => `
      <div class="form-check" data-name="${e(x.name.toLowerCase())}"><input class="form-check-input" type="checkbox" value="${e(x.id)}" id="mt-${e(x.id)}" ${gewaehlt.includes(x.id) ? 'checked' : ''}>
        <label class="form-check-label" for="mt-${e(x.id)}">${e(x.name)}</label></div>`).join('')).join('')}</div>`;
}

function sucheBinden(box) {
  box.querySelector('[data-suche]').oninput = ev => {
    const f = ev.target.value.trim().toLowerCase();
    box.querySelectorAll('[data-name]').forEach(l => l.classList.toggle('d-none', !!f && !l.dataset.name.includes(f)));
  };
}

async function schreiben(kontext, { an, betreff = '', zitat, entwurf } = {}) {
  const empfaenger = await holen('/api/app/mitteilungen/empfaenger');
  if (!empfaenger.length) { toast('Für Ihr Konto sind keine Empfänger verfügbar.', true); return; }
  const text = entwurf ? entwurf.text : zitat ? `\n\n----- ${zitat.absender || 'Ich'} schrieb: -----\n${zitat.text}` : '';
  const vorhandeneAnhaenge = entwurf?.anhaenge ?? [];
  const neueDateien = [];
  let entwurfId = entwurf?.id ?? null;
  const werte = box => ({
    empfaenger: [...box.querySelectorAll('[data-name] input:checked')].map(x => x.value),
    betreff: box.querySelector('[name=betreff]').value, text: box.querySelector('[name=text]').value, wichtig: !!box.querySelector('[name=wichtig]')?.checked,
  });
  const entwurfSpeichern = async box => {
    entwurfId = (await senden('/api/app/mitteilungen/entwurf', { id: entwurfId, ...werte(box) })).id;
    for (const d of neueDateien.splice(0)) {
      const fd = new FormData();
      fd.append('datei', d);
      await api(`/api/app/mitteilungen/${entwurfId}/anhaenge`, { methode: 'POST', formular: fd });
    }
    return entwurfId;
  };
  const nachher = ziel => { if (location.hash.startsWith('#/mitteilungen')) kontext.navigieren(ziel); };
  dialog({
    titel: entwurf ? 'Entwurf bearbeiten' : 'Neue Mitteilung', breit: true,
    inhalt: `<div class="mb-3">${empfaengerHtml(empfaenger, entwurf?.entwurfEmpfaenger ?? (an ? [an] : []))}</div>
      <div class="mb-3"><label class="form-label">Betreff</label><input class="form-control" name="betreff" maxlength="200" value="${e(entwurf?.betreff ?? betreff)}" required></div>
      <div class="mb-3"><label class="form-label">Nachricht</label><textarea class="form-control" name="text" rows="8" required>${e(text)}</textarea></div>
      <div class="d-flex flex-wrap gap-3 align-items-center">
        ${kontext.ich.kollegium ? `<div class="form-check mb-0"><input class="form-check-input" type="checkbox" name="wichtig" id="mt-wichtig" ${entwurf?.wichtig ? 'checked' : ''}><label class="form-check-label" for="mt-wichtig">Als wichtig markieren</label></div>` : ''}
        <label class="btn btn-sm btn-outline-secondary mb-0 ms-auto"><i class="bi bi-paperclip"></i>Anhang hinzufügen<input type="file" multiple hidden data-dateien></label></div>
      <div class="mt-anhaenge" data-anhangsliste>${vorhandeneAnhaenge.map(a => `<span class="mt-anhang"><i class="bi bi-paperclip"></i>${e(a.name)}<button type="button" class="btn-close ms-1" style="font-size:.6rem" data-anhang-weg="${a.id}" aria-label="Anhang entfernen"></button></span>`).join('')}</div>`,
    beimOeffnen: box => {
      sucheBinden(box);
      const zeigeDateien = () => {
        box.querySelectorAll('[data-neu-datei]').forEach(x => x.remove());
        box.querySelector('[data-anhangsliste]').insertAdjacentHTML('beforeend', neueDateien.map((d, i) => `<span class="mt-anhang" data-neu-datei><i class="bi bi-paperclip"></i>${e(d.name)} <span class="text-body-secondary fw-normal">${groesse(d.size)}</span>
          <button type="button" class="btn-close ms-1" style="font-size:.6rem" data-datei-weg="${i}" aria-label="Anhang entfernen"></button></span>`).join(''));
        box.querySelectorAll('[data-datei-weg]').forEach(b => b.onclick = () => { neueDateien.splice(Number(b.dataset.dateiWeg), 1); zeigeDateien(); });
      };
      box.querySelector('[data-dateien]').onchange = ev => {
        for (const d of ev.target.files) {
          if (d.size > 10 * 1024 * 1024) { toast(`${d.name} ist größer als 10 MB.`, true); continue; }
          if (neueDateien.length + vorhandeneAnhaenge.length >= 5) { toast('Höchstens 5 Anhänge.', true); break; }
          neueDateien.push(d);
        }
        ev.target.value = '';
        zeigeDateien();
      };
      box.querySelectorAll('[data-anhang-weg]').forEach(b => b.onclick = async () => {
        await api(`/api/app/mitteilungen/anhaenge/${b.dataset.anhangWeg}`, { methode: 'DELETE' });
        b.closest('.mt-anhang').remove();
        vorhandeneAnhaenge.splice(vorhandeneAnhaenge.findIndex(a => String(a.id) === b.dataset.anhangWeg), 1);
      });
    },
    knoepfe: [
      { text: 'Als Entwurf speichern', aktion: async box => { await entwurfSpeichern(box); toast('Entwurf gespeichert.'); nachher('#/mitteilungen/entwuerfe'); } },
      { text: 'Abbrechen' },
      {
        text: 'Senden', art: 'haupt', aktion: async box => {
          const w = werte(box);
          if (!w.empfaenger.length) { toast('Bitte mindestens einen Empfänger auswählen.', true); return false; }
          if (entwurfId || neueDateien.length) {
            const id = await entwurfSpeichern(box);
            await senden(`/api/app/mitteilungen/${id}/senden`, {});
          } else await senden('/api/app/mitteilungen', w);
          toast('Mitteilung gesendet.');
          nachher('#/mitteilungen/gesendet');
        },
      },
    ],
  });
}

// ------------------------------------------------------------------ Listen (eigene Verteiler)

async function listen(seite, kontext) {
  kontext.titel('Listen', `<button class="btn btn-primary" data-neu>${icon('plus')}Erstellen</button>`);
  const liste = await holen('/api/app/mitteilungen/listen');
  seite.innerHTML = `<p class="small text-body-secondary">Eigene Listen fassen Empfänger zusammen (Personen, Klassen, Gruppen). Beim Schreiben erscheinen sie unter „Meine Listen“.</p>
    <div class="u-tabelle-karte">${liste.length ? `<table class="u-tabelle"><thead><tr><th>Name</th><th>Mitglieder</th><th>erstellt</th><th></th></tr></thead><tbody>
      ${liste.map((l, i) => `<tr><td class="fw-semibold" style="width:auto">${e(l.name)}</td><td class="small">${e(l.namen.join(', '))}</td><td class="small text-body-secondary">${e(zeit(l.erstellt))}</td>
        <td class="text-end text-nowrap"><button class="btn btn-sm btn-outline-secondary" data-schreiben="${i}">${icon('senden')}Schreiben</button>
          <button class="btn btn-sm btn-outline-secondary" data-bearbeiten="${i}" title="Bearbeiten">${icon('stift')}</button>
          <button class="btn btn-sm btn-outline-danger" data-loeschen="${i}" title="Löschen">${icon('loeschen')}</button></td></tr>`).join('')}</tbody></table>`
      : leer('Noch keine Listen angelegt.', 'people')}</div>`;
  const neu = () => kontext.navigieren(location.hash);
  const formular = async l => {
    const empfaenger = (await holen('/api/app/mitteilungen/empfaenger')).filter(x => x.art !== 'liste');
    dialog({
      titel: l ? `Liste ${l.name}` : 'Neue Liste', breit: true,
      inhalt: `<div class="mb-3"><label class="form-label">Name</label><input class="form-control" name="name" value="${e(l?.name ?? '')}" required></div>${empfaengerHtml(empfaenger, l?.mitglieder ?? [])}`,
      beimOeffnen: sucheBinden,
      knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
        const mitglieder = [...box.querySelectorAll('[data-name] input:checked')].map(x => x.value);
        await senden(l ? `/api/app/mitteilungen/listen/${l.id}` : '/api/app/mitteilungen/listen', { name: box.querySelector('[name=name]').value, mitglieder }, l ? 'PUT' : 'POST');
        toast('Liste gespeichert.'); neu();
      } }],
    });
  };
  document.querySelector('[data-neu]').onclick = () => formular(null);
  seite.querySelectorAll('[data-bearbeiten]').forEach(b => b.onclick = () => formular(liste[Number(b.dataset.bearbeiten)]));
  seite.querySelectorAll('[data-schreiben]').forEach(b => b.onclick = () => schreiben(kontext, { an: 'liste:' + liste[Number(b.dataset.schreiben)].id }));
  seite.querySelectorAll('[data-loeschen]').forEach(b => b.onclick = async () => {
    const l = liste[Number(b.dataset.loeschen)];
    if (!await bestaetigen('Liste löschen', `Liste „${l.name}“ löschen?`)) return;
    await api(`/api/app/mitteilungen/listen/${l.id}`, { methode: 'DELETE' });
    neu();
  });
}
