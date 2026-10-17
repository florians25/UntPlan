// Meldungen an UntPlan: Abwesenheit melden, Raum buchen, Klausur eintragen, eigene Meldungen verfolgen.
import { e, holen, senden, api, toast, bestaetigen, heuteIso, plusTage, meldungText, statusMarker, leer, lade, alarm } from '../ui.js';
import { raumwahl, raumTokens } from '../raumwahl.js';

const BEREICHE = [
  { id: 'abwesenheit', text: 'Abwesenheit' },
  { id: 'raum', text: 'Raum buchen' },
  { id: 'klausur', text: 'Klausur' },
  { id: 'meine', text: 'Meine Meldungen' },
];

function stundenAuswahl(stunden, name, wert) {
  return `<select class="form-select" name="${name}">${stunden.map(s => `<option value="${s.nr}" ${s.nr === wert ? 'selected' : ''}>${e(s.bezeichnung || s.nr)}. Stunde${s.beginn ? ` (${e(s.beginn)})` : ''}</option>`).join('')}</select>`;
}

function mehrfachAuswahl(name, eintraege) {
  return `<div class="border rounded overflow-auto px-2 py-1 bg-white" data-mehrfach="${name}" style="max-height:180px">${eintraege.map(x => `
    <div class="form-check"><input class="form-check-input" type="checkbox" value="${e(x.kurz)}" id="${name}-${e(x.kurz)}">
      <label class="form-check-label" for="${name}-${e(x.kurz)}"><span class="fw-semibold">${e(x.kurz)}</span> <span class="text-body-secondary small">${e(x.name && x.name !== x.kurz ? x.name : '')}</span></label></div>`).join('')}</div>`;
}
const gewaehlt = (wurzel, name) => [...wurzel.querySelectorAll(`[data-mehrfach="${name}"] input:checked`)].map(x => x.value);
const karte = (titel, inhalt) => `<div class="card" style="max-width:860px"><div class="card-header">${titel}</div><div class="card-body">${inhalt}</div></div>`;

export async function zeigen(seite, kontext, [bereichParam]) {
  const bereich = BEREICHE.some(b => b.id === bereichParam) ? bereichParam : 'abwesenheit';
  kontext.titel({ abwesenheit: 'Abwesenheit melden', raum: 'Raum buchen', klausur: 'Prüfung eintragen', meine: 'Meine Meldungen' }[bereich]);
  const el = await kontext.elemente();
  const stunden = (el.stunden ?? []).map(s => ({ nr: s.nr, bezeichnung: s.bezeichnung, beginn: s.beginn })).filter(s => s.nr > 0);
  if (!stunden.length) for (let i = 1; i <= 10; i++) stunden.push({ nr: i, bezeichnung: String(i) });
  const letzte = stunden[stunden.length - 1].nr;

  // Die Bereiche stehen im Untermenü „Melden & Buchen“ der Navigation
  seite.innerHTML = '<div data-bereich></div>';
  const ziel = seite.querySelector('[data-bereich]');

  if (bereich === 'abwesenheit') {
    ziel.innerHTML = karte('Abwesenheit melden', `
      <p class="small text-body-secondary">Die Meldung wird an die Vertretungsplanung übermittelt und dort als Abwesenheit übernommen.</p>
      <form>
        <div class="row g-3 mb-3">
          <div class="col-sm-6"><label class="form-label">Von</label><input type="date" class="form-control" name="von" value="${heuteIso()}" required></div>
          <div class="col-sm-6"><label class="form-label">Bis</label><input type="date" class="form-control" name="bis" value="${heuteIso()}" required></div>
        </div>
        <div class="form-check mb-3"><input class="form-check-input" type="checkbox" name="ganz" id="ganz" checked><label class="form-check-label" for="ganz">ganztägig</label></div>
        <div class="row g-3 mb-3 d-none" data-stunden>
          <div class="col-sm-6"><label class="form-label">ab</label>${stundenAuswahl(stunden, 'stundeVon', 1)}</div>
          <div class="col-sm-6"><label class="form-label">bis</label>${stundenAuswahl(stunden, 'stundeBis', letzte)}</div>
        </div>
        <div class="mb-3"><label class="form-label">Grund</label><select class="form-select" name="grund"><option>Krankheit</option><option>Fortbildung</option><option>Dienstlicher Termin</option><option>Klassenfahrt / Exkursion</option><option>Sonderurlaub</option><option>Sonstiges</option></select></div>
        <div class="mb-3"><label class="form-label">Hinweise für die Vertretung <span class="text-body-secondary">(optional)</span></label><textarea class="form-control" name="text" rows="3"></textarea></div>
        <button class="btn btn-primary" type="submit">Abwesenheit melden</button>
      </form>`);
    const form = ziel.querySelector('form');
    form.ganz.onchange = () => ziel.querySelector('[data-stunden]').classList.toggle('d-none', form.ganz.checked);
    form.von.onchange = () => { if (form.bis.value < form.von.value) form.bis.value = form.von.value; };
    form.onsubmit = async ev => {
      ev.preventDefault();
      try {
        await senden('/api/app/meldungen/abwesenheit', {
          von: form.von.value, bis: form.bis.value, grund: form.grund.value, text: form.text.value,
          stundeVon: form.ganz.checked ? null : Number(form.stundeVon.value), stundeBis: form.ganz.checked ? null : Number(form.stundeBis.value),
        });
        toast('Abwesenheit gemeldet.');
        kontext.navigieren('#/melden/meine');
      } catch (err) { kontext.fehler(err); }
    };
  }

  if (bereich === 'raum') {
    ziel.innerHTML = karte('Raum buchen', `
      <form>
        <div class="row g-3 mb-3">
          <div class="col-sm-4"><label class="form-label">Datum</label><input type="date" class="form-control" name="datum" value="${plusTage(heuteIso(), 1)}" min="${heuteIso()}" required></div>
          <div class="col-sm-4"><label class="form-label">Von</label>${stundenAuswahl(stunden, 'stundeVon', 1)}</div>
          <div class="col-sm-4"><label class="form-label">Bis</label>${stundenAuswahl(stunden, 'stundeBis', 1)}</div>
        </div>
        <div class="mb-3"><label class="form-label">Freie Räume</label><div data-raeume>${lade()}</div></div>
        <div class="row g-3 mb-3">
          <div class="col-sm-6"><label class="form-label">Titel</label><input class="form-control" name="titel"></div>
          <div class="col-sm-6"><label class="form-label">Klasse(n) <span class="text-body-secondary">(optional)</span></label><input class="form-control" name="klassen" placeholder="z. B. 7a, 7b" list="klassenliste"></div>
        </div>
        <datalist id="klassenliste">${el.klassen.map(k => `<option value="${e(k.kurz)}">`).join('')}</datalist>
        <div class="mb-3"><label class="form-label">Bemerkung <span class="text-body-secondary">(optional)</span></label><input class="form-control" name="text"></div>
        <button class="btn btn-primary" type="submit">Raum buchen</button>
        <div class="form-text">Die Buchung erscheint sofort als „vorgemerkt“ im Raumplan und wird von UntPlan übernommen.</div>
      </form>`);
    const form = ziel.querySelector('form');
    let raum = null;
    const laden = async () => {
      if (Number(form.stundeBis.value) < Number(form.stundeVon.value)) form.stundeBis.value = form.stundeVon.value;
      const box = ziel.querySelector('[data-raeume]');
      raum = null;
      try {
        const frei = await holen(`/api/app/meldungen/freieraeume?datum=${form.datum.value}&von=${form.stundeVon.value}&bis=${form.stundeBis.value}`);
        box.innerHTML = frei.length ? `<div class="d-flex flex-wrap gap-2">${frei.map(r => `<input type="radio" class="btn-check" name="raumwahl" id="r-${e(r.kurz)}" value="${e(r.kurz)}" autocomplete="off">
            <label class="btn btn-sm btn-outline-secondary" for="r-${e(r.kurz)}" title="${e(r.name ?? '')}">${e(r.kurz)}</label>`).join('')}</div>`
          : alarm('warning', 'In diesem Zeitraum ist kein Raum frei.');
        box.querySelectorAll('[name=raumwahl]').forEach(b => b.onchange = () => { raum = b.value; });
      } catch (err) { box.innerHTML = alarm('danger', err.message); }
    };
    form.datum.onchange = form.stundeVon.onchange = form.stundeBis.onchange = laden;
    laden();
    form.onsubmit = async ev => {
      ev.preventDefault();
      if (!raum) { toast('Bitte einen freien Raum auswählen.', true); return; }
      try {
        await senden('/api/app/meldungen/raumbuchung', {
          datum: form.datum.value, stundeVon: Number(form.stundeVon.value), stundeBis: Number(form.stundeBis.value), raum,
          klassen: form.klassen.value.split(/[,;\s]+/).filter(Boolean), titel: form.titel.value, text: form.text.value,
        });
        toast(`Raum ${raum} gebucht.`);
        kontext.navigieren('#/melden/meine');
      } catch (err) { kontext.fehler(err); }
    };
  }

  if (bereich === 'klausur') {
    ziel.innerHTML = karte('Klausur eintragen', `
      <form>
        <div class="row g-3 mb-3">
          <div class="col-sm-4"><label class="form-label">Datum</label><input type="date" class="form-control" name="datum" value="${plusTage(heuteIso(), 7)}" min="${heuteIso()}" required></div>
          <div class="col-sm-4"><label class="form-label">Von</label>${stundenAuswahl(stunden, 'stundeVon', 3)}</div>
          <div class="col-sm-4"><label class="form-label">Bis</label>${stundenAuswahl(stunden, 'stundeBis', 4)}</div>
        </div>
        <div class="mb-3"><label class="form-label">Titel</label><input class="form-control" name="titel" placeholder="z. B. Deutsch GK" required></div>
        <div class="row g-3 mb-3">
          <div class="col-md-4"><label class="form-label">Klassen / Kurse</label>${mehrfachAuswahl('klassen', el.klassen)}</div>
          <div class="col-md-4"><label class="form-label" for="mk-raeume">Räume <span class="text-body-secondary">(optional)</span></label><input class="form-control" id="mk-raeume" name="raeume"></div>
          <div class="col-md-4"><label class="form-label">Aufsicht <span class="text-body-secondary">(optional)</span></label>${mehrfachAuswahl('aufsicht', el.lehrer)}</div>
        </div>
        <div class="mb-3"><label class="form-label">Bemerkung <span class="text-body-secondary">(optional)</span></label><input class="form-control" name="text"></div>
        <button class="btn btn-primary" type="submit">Klausur eintragen</button>
      </form>`);
    const form = ziel.querySelector('form');
    form.stundeVon.onchange = () => { if (Number(form.stundeBis.value) < Number(form.stundeVon.value)) form.stundeBis.value = form.stundeVon.value; };
    // Raumwahl mit Suche und Belegung zur gewählten Zeit (Datum, Von, Bis)
    raumwahl(form.raeume, { wurzel: form, felder: { datum: 'datum', von: 'stundeVon', bis: 'stundeBis' } });
    form.onsubmit = async ev => {
      ev.preventDefault();
      const klassen = gewaehlt(ziel, 'klassen');
      if (!klassen.length) { toast('Bitte mindestens eine Klasse auswählen.', true); return; }
      try {
        await senden('/api/app/meldungen/klausur', {
          datum: form.datum.value, stundeVon: Number(form.stundeVon.value), stundeBis: Number(form.stundeBis.value),
          klassen, raeume: raumTokens(form.raeume.value), aufsicht: gewaehlt(ziel, 'aufsicht'), titel: form.titel.value, text: form.text.value,
        });
        toast('Klausur eingetragen.');
        kontext.navigieren('#/melden/meine');
      } catch (err) { kontext.fehler(err); }
    };
  }

  if (bereich === 'meine') {
    const liste = await holen('/api/app/meldungen');
    ziel.innerHTML = `<div class="card"><div class="card-header">Meine Meldungen</div>
      ${liste.length ? `<div class="table-responsive"><table class="table table-sm table-hover table-card align-middle"><thead><tr><th>Meldung</th><th>Erstellt</th><th>Status</th><th></th></tr></thead><tbody>
        ${liste.map(m => `<tr><td>${e(meldungText(m))}${m.text && m.typ !== 'nachricht' ? `<div class="small text-body-secondary">${e(m.text)}</div>` : ''}</td>
          <td class="text-body-secondary small text-nowrap">${e(new Date(m.erstellt.replace(' ', 'T')).toLocaleString('de-DE', { dateStyle: 'short', timeStyle: 'short' }))}</td>
          <td>${statusMarker(m.status)}</td>
          <td class="text-end">${m.typ === 'klausur' && (m.von ?? '') >= heuteIso() ? `<button class="btn btn-sm btn-outline-danger" data-klausur-loeschen="${e(m.id)}">Löschen</button>`
            : m.status === 'offen' ? `<button class="btn btn-sm btn-outline-danger" data-zurueck="${e(m.id)}">Zurückziehen</button>` : ''}</td></tr>`).join('')}
      </tbody></table></div>` : `<div class="card-body py-0">${leer('Noch keine Meldungen.')}</div>`}</div>`;
    // Prüfung löschen – vor der Übernahme zurückziehen, danach Löschmeldung an UntPlan
    ziel.querySelectorAll('[data-klausur-loeschen]').forEach(b => b.onclick = async () => {
      const m = liste.find(x => x.id === b.dataset.klausurLoeschen);
      const { pruefungLoeschen } = await import('./pruefungen.js');
      if (await pruefungLoeschen({ datum: m.von, stundeVon: m.stundeVon ?? 1, stundeBis: m.stundeBis ?? m.stundeVon ?? 1, klassen: m.klassen ?? [], titel: m.titel })) kontext.navigieren(location.hash);
    });
    ziel.querySelectorAll('[data-zurueck]').forEach(b => b.onclick = async () => {
      if (!await bestaetigen('Meldung zurückziehen', 'Soll die Meldung zurückgezogen werden?', 'Zurückziehen')) return;
      try { await api(`/api/app/meldungen/${encodeURIComponent(b.dataset.zurueck)}`, { methode: 'DELETE' }); toast('Meldung zurückgezogen.'); kontext.navigieren(location.hash); }
      catch (err) { kontext.fehler(err); }
    });
  }
}

