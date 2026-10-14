// Administration: Synchronisation mit UntPlan, Benutzer, Rückmeldungen, Nachrichten zum Tag, Monitore, Protokoll.
import { e, icon, holen, senden, api, toast, dialog, bestaetigen, formularWerte, heuteIso, datumKurz, meldungText, statusMarker, leer, alarm } from '../ui.js';

const BEREICHE = [
  { id: 'sync', text: 'Übersicht', admin: true },
  { id: 'benutzer', text: 'Benutzer', admin: true },
  { id: 'rueckmeldungen', text: 'Rückmeldungen', admin: true },
  { id: 'nachrichten', text: 'Nachrichten zum Tag', admin: false },
  { id: 'monitore', text: 'Monitore', admin: true },
  { id: 'protokoll', text: 'Protokoll', admin: true },
];
const ROLLEN = { admin: 'Administration', leitung: 'Schulleitung', lehrer: 'Lehrkraft', schueler: 'Klasse / Schüler' };
const kurzZeit = s => s ? new Date(s.replace(' ', 'T')).toLocaleString('de-DE', { dateStyle: 'short', timeStyle: 'short' }) : '';

function kopieren(text) {
  navigator.clipboard?.writeText(text).then(() => toast('In die Zwischenablage kopiert.'), () => toast('Kopieren nicht möglich – bitte manuell markieren.', true));
}

function herunterladen(name, inhalt, typ = 'text/csv;charset=utf-8') {
  const url = URL.createObjectURL(new Blob(['﻿' + inhalt], { type: typ }));
  const a = document.createElement('a');
  a.href = url; a.download = name;
  document.body.append(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 2000);
}

/** Feld mit Wert und Kopierknopf. */
const kopierfeld = (wert, anzeige = wert) => `<div class="input-group"><input class="form-control font-monospace bg-body-tertiary" value="${e(anzeige)}" readonly>
  <button class="btn btn-outline-secondary" type="button" data-kopie="${e(wert)}" title="Kopieren">${icon('kopieren')}</button></div>`;

export async function zeigen(seite, kontext, [bereichParam]) {
  const ich = kontext.ich;
  const erlaubt = BEREICHE.filter(b => ich.admin || !b.admin);
  const bereich = erlaubt.find(b => b.id === bereichParam) ?? erlaubt[0];
  kontext.titel(ich.admin ? 'Administration' : 'Nachrichten zum Tag');
  seite.innerHTML = `${erlaubt.length > 1 ? `<ul class="nav nav-tabs mb-3">${erlaubt.map(b => `<li class="nav-item"><a class="nav-link ${b === bereich ? 'active' : ''}" href="#/admin/${b.id}">${b.text}</a></li>`).join('')}</ul>` : ''}<div data-bereich></div>`;
  const ziel = seite.querySelector('[data-bereich]');
  const neu = () => kontext.navigieren(location.hash);
  await ({ sync, benutzer, rueckmeldungen, nachrichten, monitore, protokoll })[bereich.id](ziel, kontext, neu);
  ziel.querySelectorAll('[data-kopie]').forEach(b => b.onclick = () => kopieren(b.dataset.kopie));
}

// ------------------------------------------------------------------ Übersicht und Synchronisation

async function sync(ziel, kontext, neu) {
  const u = await holen('/api/app/admin/uebersicht');
  let frisch = null;
  try { frisch = JSON.parse(sessionStorage.getItem('untplan:neuerSchluessel') ?? 'null'); sessionStorage.removeItem('untplan:neuerSchluessel'); } catch { /* egal */ }
  const p = u.paket;
  const anzahl = r => u.benutzer[r] ?? 0;
  ziel.innerHTML = `
    ${frisch ? alarm('success', '<strong>UntPlanWeb ist eingerichtet.</strong> Tragen Sie die Verbindungsdaten in UntPlan unter <em>UntPlanWeb → Einstellungen</em> ein. Der API-Schlüssel wird nur einmal angezeigt.', true) : ''}
    <div class="row g-3">
      <div class="col-xl-6">
        <div class="card h-100"><div class="card-header">Verbindung mit UntPlan</div><div class="card-body">
          <div class="mb-3"><label class="form-label small text-body-secondary mb-1">Adresse</label>${kopierfeld(u.adresse)}</div>
          <div class="mb-3"><label class="form-label small text-body-secondary mb-1">Schul-ID</label>${kopierfeld(u.schulId || '')}</div>
          <div class="mb-3"><label class="form-label small text-body-secondary mb-1">API-Schlüssel</label><div data-schluessel>
            ${frisch?.apiSchluessel ? kopierfeld(frisch.apiSchluessel) : `<input class="form-control bg-body-tertiary" value="${u.apiSchluesselGesetzt ? 'gespeichert (wird aus Sicherheitsgründen nicht angezeigt)' : 'noch nicht erzeugt'}" readonly>`}</div></div>
          <button class="btn btn-outline-secondary btn-sm" data-neuer-schluessel>${icon('schluessel', 'me-1')}${u.apiSchluesselGesetzt ? 'Neuen Schlüssel erzeugen' : 'Schlüssel erzeugen'}</button>
          <p class="small text-body-secondary mt-3 mb-0">In UntPlan unter <strong>UntPlanWeb → Einstellungen</strong> Adresse, Schul-ID und API-Schlüssel eintragen.
            Danach überträgt <strong>Exportieren</strong> die Daten; <strong>Importieren</strong> holt Abwesenheiten, Raumbuchungen und Klausuren ab.</p>
        </div></div>
      </div>
      <div class="col-xl-6">
        <div class="card h-100"><div class="card-header d-flex align-items-center">Datenstand
          <label class="btn btn-outline-secondary btn-sm ms-auto mb-0">${icon('hochladen', 'me-1')}Paket hochladen<input type="file" accept=".json,application/json" data-paket hidden></label></div>
          ${p ? `<table class="table table-sm table-card mb-0"><tbody>
            <tr><th class="fw-normal text-body-secondary w-25">Empfangen</th><td>${e(kurzZeit(p.empfangen))}</td></tr>
            <tr><th class="fw-normal text-body-secondary">Revision</th><td>${p.revision}${p.programmversion ? ` <span class="text-body-secondary">· UntPlan ${e(p.programmversion)}</span>` : ''}</td></tr>
            <tr><th class="fw-normal text-body-secondary">Schuljahr</th><td>${e(p.schuljahr)}</td></tr>
            <tr><th class="fw-normal text-body-secondary">Zeitraum</th><td>${p.von ? `${e(datumKurz(p.von))}${p.von.slice(0, 4)} – ${e(datumKurz(p.bis))}${p.bis.slice(0, 4)}` : '–'}</td></tr>
            <tr><th class="fw-normal text-body-secondary">Stammdaten</th><td>${p.klassen} Klassen, ${p.lehrer} Lehrkräfte, ${p.raeume} Räume</td></tr>
            <tr><th class="fw-normal text-body-secondary">Pläne</th><td>${p.perioden} Perioden, ${p.tage} tagesaktuelle Tage, ${p.vertretungen} Vertretungen</td></tr>
            <tr><th class="fw-normal text-body-secondary">Benutzer</th><td>${anzahl('lehrer') + anzahl('leitung') + anzahl('admin')} Kollegium, ${anzahl('schueler')} Klassenkonten</td></tr>
            <tr><th class="fw-normal text-body-secondary">Rückmeldungen</th><td>${u.offeneRueckmeldungen ? `<a href="#/admin/rueckmeldungen">${u.offeneRueckmeldungen} offen</a>` : 'keine offenen'}</td></tr>
          </tbody></table>` : `<div class="card-body">${leer('Noch keine Daten empfangen.')}</div>`}
        </div>
      </div>
      <div class="col-xl-6">
        <div class="card"><div class="card-header">Schule</div><div class="card-body">
          <form data-schule><div class="row g-3 mb-3">
            <div class="col-sm-7"><label class="form-label">Name der Schule</label><input class="form-control" name="schulname" value="${e(u.schulname)}" required></div>
            <div class="col-sm-5"><label class="form-label">Schul-ID</label><input class="form-control" name="schulId" value="${e(u.schulId)}" pattern="[a-z0-9-]+" title="Kleinbuchstaben, Ziffern und Bindestriche"></div></div>
            <div class="d-flex align-items-center"><button class="btn btn-primary" type="submit">Speichern</button><span class="ms-auto small text-body-secondary">Version ${e(u.version ?? '')}</span></div>
          </form></div></div>
      </div>
    </div>`;

  ziel.querySelector('[data-neuer-schluessel]').onclick = async () => {
    if (u.apiSchluesselGesetzt && !await bestaetigen('Neuen API-Schlüssel erzeugen', 'Der bisherige Schlüssel wird ungültig. UntPlan muss danach mit dem neuen Schlüssel eingerichtet werden.', 'Neu erzeugen')) return;
    const r = await senden('/api/app/admin/apischluessel', {});
    const box = ziel.querySelector('[data-schluessel]');
    box.innerHTML = kopierfeld(r.apiSchluessel);
    box.querySelector('[data-kopie]').onclick = () => kopieren(r.apiSchluessel);
    toast('Neuer Schlüssel erzeugt.');
  };
  ziel.querySelector('[data-paket]').onchange = async ev => {
    const datei = ev.target.files[0];
    if (!datei) return;
    const fd = new FormData();
    fd.append('datei', datei);
    try {
      const r = await api('/api/app/admin/paket', { methode: 'POST', formular: fd });
      toast(`Paket übernommen (Revision ${r.revision}).`);
      await kontext.elemente(true);
      neu();
    } catch (err) { kontext.fehler(err); }
  };
  ziel.querySelector('[data-schule]').onsubmit = async ev => {
    ev.preventDefault();
    try { await senden('/api/app/admin/schule', formularWerte(ev.target), 'PUT'); toast('Gespeichert.'); await kontext.sitzungNeu(); neu(); }
    catch (err) { kontext.fehler(err); }
  };
}

// ------------------------------------------------------------------ Benutzer

function passwortHinweis(login, pw) {
  dialog({
    titel: 'Startpasswort', inhalt: `<p>Startpasswort für <strong>${e(login)}</strong>:</p>${kopierfeld(pw)}
      <div class="form-text">Beim ersten Anmelden muss ein eigenes Passwort vergeben werden.</div>`,
    beimOeffnen: box => { box.querySelector('[data-kopie]').onclick = () => kopieren(pw); },
    knoepfe: [{ text: 'Schließen', art: 'haupt' }],
  });
}

async function benutzer(ziel, kontext, neu) {
  const [liste, el] = await Promise.all([holen('/api/app/admin/benutzer'), kontext.elemente()]);
  ziel.innerHTML = `<div class="d-flex flex-wrap align-items-center gap-2 mb-3">
      <input type="search" class="form-control" data-filter placeholder="Suchen" style="width:220px">
      <select class="form-select" data-rolle style="width:auto"><option value="">Alle Rollen</option>${Object.entries(ROLLEN).map(([r, n]) => `<option value="${r}">${n}</option>`).join('')}</select>
      <div class="ms-auto d-flex gap-2">
        <button class="btn btn-outline-secondary" data-stammdaten>${icon('benutzer', 'me-1')}Konten aus Stammdaten</button>
        <button class="btn btn-primary" data-neu>${icon('plus', 'me-1')}Benutzer anlegen</button>
      </div>
    </div>
    <div class="card"><div class="table-responsive"><table class="table table-sm table-hover table-card align-middle"><thead><tr><th>Benutzername</th><th>Name</th><th>Rolle</th><th>Kürzel</th><th>Letzte Anmeldung</th><th></th></tr></thead><tbody>
      ${liste.map(b => `<tr data-zeile data-rolle="${e(b.rolle)}" data-text="${e((b.login + ' ' + b.anzeigename + ' ' + b.element).toLowerCase())}">
        <td class="fw-semibold">${e(b.login)}${b.gesperrt ? ' <span class="badge text-bg-danger fw-normal">gesperrt</span>' : ''}${b.passwortAendern ? ' <span class="badge text-bg-warning fw-normal">Startpasswort</span>' : ''}</td>
        <td>${e(b.anzeigename)}${b.email ? `<div class="small text-body-secondary">${e(b.email)}</div>` : ''}</td>
        <td>${e(b.rollenname)}</td><td>${e(b.element)}</td>
        <td class="text-body-secondary small">${b.letzteAnmeldung ? e(kurzZeit(b.letzteAnmeldung)) : 'nie'}</td>
        <td class="text-end text-nowrap">
          <div class="btn-group btn-group-sm">
            <button class="btn btn-outline-secondary" data-bearbeiten="${b.id}" title="Bearbeiten">${icon('stift')}</button>
            <button class="btn btn-outline-secondary" data-passwort="${b.id}" title="Passwort zurücksetzen">${icon('schluessel')}</button>
            ${b.id !== kontext.ich.id ? `<button class="btn btn-outline-danger" data-loeschen="${b.id}" title="Löschen">${icon('loeschen')}</button>` : ''}
          </div></td></tr>`).join('')}
    </tbody></table></div></div>
    <datalist id="elementliste">${[...el.lehrer, ...el.klassen].map(x => `<option value="${e(x.kurz)}">${e(x.name ?? '')}</option>`).join('')}</datalist>`;

  const filtern = () => {
    const f = ziel.querySelector('[data-filter]').value.trim().toLowerCase();
    const rolle = ziel.querySelector('select[data-rolle]').value;
    ziel.querySelectorAll('[data-zeile]').forEach(z => z.classList.toggle('d-none', (rolle && z.dataset.rolle !== rolle) || (f && !z.dataset.text.includes(f))));
  };
  ziel.querySelector('[data-filter]').oninput = filtern;
  ziel.querySelector('select[data-rolle]').onchange = filtern;

  const formular = (b = {}) => `
      <div class="row g-3 mb-3">
        <div class="col-sm-6"><label class="form-label">Benutzername</label><input class="form-control" name="login" value="${e(b.login ?? '')}" ${b.id ? 'disabled' : 'required'} autocomplete="off"></div>
        <div class="col-sm-6"><label class="form-label">Anzeigename</label><input class="form-control" name="anzeigename" value="${e(b.anzeigename ?? '')}"></div>
        <div class="col-sm-6"><label class="form-label">Rolle</label><select class="form-select" name="rolle">${Object.entries(ROLLEN).map(([r, n]) => `<option value="${r}" ${r === (b.rolle ?? 'lehrer') ? 'selected' : ''}>${n}</option>`).join('')}</select></div>
        <div class="col-sm-6"><label class="form-label">Kürzel (Lehrkraft bzw. Klasse)</label><input class="form-control" name="element" value="${e(b.element ?? '')}" list="elementliste" autocomplete="off"></div>
        <div class="col-12"><label class="form-label">E-Mail <span class="text-body-secondary">(optional)</span></label><input class="form-control" name="email" type="email" value="${e(b.email ?? '')}"></div>
      </div>
      ${b.id ? `<div class="form-check"><input class="form-check-input" type="checkbox" name="gesperrt" id="gesperrt" ${b.gesperrt ? 'checked' : ''}><label class="form-check-label" for="gesperrt">Konto gesperrt</label></div>`
        : '<div><label class="form-label">Passwort</label><input class="form-control" name="passwort" type="password" minlength="8" autocomplete="new-password"><div class="form-text">Leer lassen, um ein Startpasswort zu erzeugen.</div></div>'}`;

  ziel.querySelector('[data-neu]').onclick = () => dialog({
    titel: 'Benutzer anlegen', inhalt: formular(), knoepfe: [{ text: 'Abbrechen' }, {
      text: 'Anlegen', art: 'haupt', aktion: async box => {
        const r = await senden('/api/app/admin/benutzer', { ...formularWerte(box), gesperrt: false });
        toast('Benutzer angelegt.');
        neu();
        if (r.passwort) passwortHinweis(r.benutzer.login, r.passwort);
      },
    }],
  });
  ziel.querySelectorAll('[data-bearbeiten]').forEach(k => k.onclick = () => {
    const b = liste.find(x => x.id === Number(k.dataset.bearbeiten));
    dialog({
      titel: `Benutzer ${b.login}`, inhalt: formular(b), knoepfe: [{ text: 'Abbrechen' }, {
        text: 'Speichern', art: 'haupt', aktion: async box => {
          await senden(`/api/app/admin/benutzer/${b.id}`, formularWerte(box), 'PUT');
          toast('Gespeichert.');
          neu();
        },
      }],
    });
  });
  ziel.querySelectorAll('[data-passwort]').forEach(k => k.onclick = async () => {
    const b = liste.find(x => x.id === Number(k.dataset.passwort));
    if (!await bestaetigen('Passwort zurücksetzen', `Für ${b.login} ein neues Startpasswort erzeugen?`, 'Zurücksetzen')) return;
    const r = await senden(`/api/app/admin/benutzer/${b.id}/passwort`, {});
    passwortHinweis(b.login, r.passwort);
    neu();
  });
  ziel.querySelectorAll('[data-loeschen]').forEach(k => k.onclick = async () => {
    const b = liste.find(x => x.id === Number(k.dataset.loeschen));
    if (!await bestaetigen('Benutzer löschen', `${b.login} wirklich löschen?`)) return;
    await api(`/api/app/admin/benutzer/${b.id}`, { methode: 'DELETE' });
    toast('Benutzer gelöscht.');
    neu();
  });
  ziel.querySelector('[data-stammdaten]').onclick = () => dialog({
    titel: 'Konten aus Stammdaten anlegen',
    inhalt: `<p>Für alle Lehrkräfte bzw. Klassen ohne Konto wird ein Konto mit Startpasswort angelegt
      (Benutzername: Lehrerkürzel bzw. <code>klasse-7a</code>).</p>
      <div class="form-check"><input class="form-check-input" type="checkbox" name="lehrer" id="sd-l" checked><label class="form-check-label" for="sd-l">Lehrkräfte (${el.lehrer.length})</label></div>
      <div class="form-check"><input class="form-check-input" type="checkbox" name="klassen" id="sd-k"><label class="form-check-label" for="sd-k">Klassenkonten (${el.klassen.length})</label></div>`,
    knoepfe: [{ text: 'Abbrechen' }, {
      text: 'Konten anlegen', art: 'haupt', aktion: async box => {
        const r = await senden('/api/app/admin/benutzer/stammdaten', formularWerte(box));
        neu();
        dialog({
          titel: `${r.anzahl} Konten angelegt`, breit: true,
          inhalt: r.anzahl ? `${alarm('warning', 'Die Startpasswörter werden nur jetzt angezeigt. Bitte die Liste herunterladen und sicher verteilen.')}
            <div class="table-responsive" style="max-height:340px"><table class="table table-sm align-middle"><thead><tr><th>Benutzername</th><th>Startpasswort</th><th>Rolle</th><th>Name</th></tr></thead><tbody>
            ${r.konten.map(k => `<tr><td class="fw-semibold">${e(k.login)}</td><td class="font-monospace">${e(k.passwort)}</td><td>${e(k.rolle)}</td><td>${e(k.name)}</td></tr>`).join('')}</tbody></table></div>`
            : '<p class="mb-0">Alle Lehrkräfte bzw. Klassen haben bereits ein Konto.</p>',
          knoepfe: r.anzahl ? [{ text: 'Liste herunterladen (CSV)', aktion: () => { herunterladen(`untplanweb-konten-${heuteIso()}.csv`, r.csv); return false; } }, { text: 'Schließen', art: 'haupt' }] : [{ text: 'OK', art: 'haupt' }],
        });
      },
    }],
  });
}

// ------------------------------------------------------------------ Rückmeldungen

async function rueckmeldungen(ziel, kontext, neu) {
  const liste = await holen('/api/app/admin/rueckmeldungen');
  ziel.innerHTML = `<p class="small text-body-secondary">Rückmeldungen holt UntPlan beim Datenaustausch ab und quittiert sie nach der Übernahme. Der Status kann hier bei Bedarf manuell geändert werden.</p>
    <div class="card">${liste.length ? `<div class="table-responsive"><table class="table table-sm table-hover table-card align-middle"><thead><tr><th>Meldung</th><th>Von</th><th>Erstellt</th><th>Status</th><th></th></tr></thead><tbody>
      ${liste.map(m => `<tr><td>${e(meldungText(m))}${m.text && m.typ !== 'nachricht' ? `<div class="small text-body-secondary">${e(m.text)}</div>` : ''}</td>
        <td>${e(m.absender)}</td><td class="text-body-secondary small text-nowrap">${e(kurzZeit(m.erstellt))}</td>
        <td>${statusMarker(m.status)}</td>
        <td class="text-end text-nowrap"><select class="form-select form-select-sm d-inline-block w-auto" data-status="${e(m.id)}">
          ${['offen', 'quittiert', 'abgelehnt'].map(s => `<option value="${s}" ${s === m.status ? 'selected' : ''}>${s}</option>`).join('')}</select>
          <button class="btn btn-sm btn-outline-danger ms-1" data-loeschen="${e(m.id)}" title="Löschen">${icon('loeschen')}</button></td></tr>`).join('')}
    </tbody></table></div>` : `<div class="card-body py-0">${leer('Keine Rückmeldungen.')}</div>`}</div>`;
  ziel.querySelectorAll('[data-status]').forEach(s => s.onchange = async () => {
    try { await senden(`/api/app/admin/rueckmeldungen/${encodeURIComponent(s.dataset.status)}`, { status: s.value }, 'PUT'); toast('Gespeichert.'); neu(); }
    catch (err) { kontext.fehler(err); }
  });
  ziel.querySelectorAll('[data-loeschen]').forEach(b => b.onclick = async () => {
    if (!await bestaetigen('Rückmeldung löschen', 'Diese Rückmeldung endgültig löschen?')) return;
    await api(`/api/app/admin/rueckmeldungen/${encodeURIComponent(b.dataset.loeschen)}`, { methode: 'DELETE' });
    neu();
  });
}

// ------------------------------------------------------------------ Nachrichten zum Tag

const ZIELGRUPPEN = { alle: 'Alle', lehrer: 'Nur Kollegium', schueler: 'Nur Schüler' };

async function nachrichten(ziel, kontext, neu) {
  const liste = await holen('/api/app/admin/nachrichten');
  ziel.innerHTML = `<div class="d-flex flex-wrap align-items-center gap-2 mb-3"><p class="small text-body-secondary mb-0">Nachrichten erscheinen im Vertretungsplan sowie wahlweise auf der Übersicht und den Monitoren. Tagestexte aus UntPlan werden zusätzlich angezeigt.</p>
      <button class="btn btn-primary ms-auto" data-neu>${icon('plus', 'me-1')}Nachricht</button></div>
    <div class="card">${liste.length ? `<div class="table-responsive"><table class="table table-sm table-hover table-card align-middle"><thead><tr><th>Zeitraum</th><th>Text</th><th>Für</th><th>Anzeige</th><th></th></tr></thead><tbody>
      ${liste.map(n => `<tr class="${n.bis < heuteIso() ? 'text-body-secondary' : ''}"><td class="text-nowrap">${e(datumKurz(n.von))}${n.bis !== n.von ? ' – ' + e(datumKurz(n.bis)) : ''}</td>
        <td>${e(n.text)}</td><td>${e(ZIELGRUPPEN[n.zielgruppe] ?? n.zielgruppe)}</td>
        <td class="small">${['Vertretungsplan', n.startseite ? 'Übersicht' : '', n.monitor ? 'Monitor' : ''].filter(Boolean).join(', ')}</td>
        <td class="text-end text-nowrap"><div class="btn-group btn-group-sm"><button class="btn btn-outline-secondary" data-bearbeiten="${n.id}">${icon('stift')}</button>
          <button class="btn btn-outline-danger" data-loeschen="${n.id}">${icon('loeschen')}</button></div></td></tr>`).join('')}
    </tbody></table></div>` : `<div class="card-body py-0">${leer('Keine Nachrichten.')}</div>`}</div>`;
  const formular = (n = {}) => `
      <div class="row g-3 mb-3">
        <div class="col-sm-4"><label class="form-label">Von</label><input type="date" class="form-control" name="von" value="${e(n.von ?? heuteIso())}" required></div>
        <div class="col-sm-4"><label class="form-label">Bis</label><input type="date" class="form-control" name="bis" value="${e(n.bis ?? heuteIso())}" required></div>
        <div class="col-sm-4"><label class="form-label">Für</label><select class="form-select" name="zielgruppe">${Object.entries(ZIELGRUPPEN).map(([z, t]) => `<option value="${z}" ${z === (n.zielgruppe ?? 'alle') ? 'selected' : ''}>${t}</option>`).join('')}</select></div>
      </div>
      <div class="mb-3"><label class="form-label">Text</label><textarea class="form-control" name="text" rows="4" required>${e(n.text ?? '')}</textarea></div>
      <div class="form-check"><input class="form-check-input" type="checkbox" name="startseite" id="n-s" ${n.startseite ?? true ? 'checked' : ''}><label class="form-check-label" for="n-s">auf der Übersicht anzeigen</label></div>
      <div class="form-check"><input class="form-check-input" type="checkbox" name="monitor" id="n-m" ${n.monitor ?? true ? 'checked' : ''}><label class="form-check-label" for="n-m">auf den Monitoren anzeigen</label></div>`;
  const bearbeiten = n => dialog({
    titel: n ? 'Nachricht bearbeiten' : 'Neue Nachricht', inhalt: formular(n),
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
      await senden(n ? `/api/app/admin/nachrichten/${n.id}` : '/api/app/admin/nachrichten', formularWerte(box), n ? 'PUT' : 'POST');
      toast('Gespeichert.'); neu();
    } }],
  });
  ziel.querySelector('[data-neu]').onclick = () => bearbeiten(null);
  ziel.querySelectorAll('[data-bearbeiten]').forEach(b => b.onclick = () => bearbeiten(liste.find(n => n.id === Number(b.dataset.bearbeiten))));
  ziel.querySelectorAll('[data-loeschen]').forEach(b => b.onclick = async () => {
    if (!await bestaetigen('Nachricht löschen', 'Diese Nachricht löschen?')) return;
    await api(`/api/app/admin/nachrichten/${b.dataset.loeschen}`, { methode: 'DELETE' });
    neu();
  });
}

// ------------------------------------------------------------------ Monitore

async function monitore(ziel, kontext, neu) {
  const liste = await holen('/api/app/admin/monitore');
  ziel.innerHTML = `<div class="d-flex flex-wrap align-items-center gap-2 mb-3"><p class="small text-body-secondary mb-0">Ein Monitor zeigt den Vertretungsplan ohne Anmeldung über einen geheimen Link an, z. B. im Eingangsbereich (Browser im Vollbild).</p>
      <button class="btn btn-primary ms-auto" data-neu>${icon('plus', 'me-1')}Monitor</button></div>
    <div class="card">${liste.length ? `<div class="table-responsive"><table class="table table-sm table-hover table-card align-middle"><thead><tr><th>Name</th><th>Version</th><th>Tage</th><th>Link</th><th></th></tr></thead><tbody>
      ${liste.map(m => `<tr><td class="fw-semibold">${e(m.name)}</td><td>${m.version === 'lehrer' ? 'Lehrerversion' : 'Schülerversion'}</td><td>${m.tage}</td>
        <td style="min-width:280px">${kopierfeld(m.link)}</td>
        <td class="text-end text-nowrap"><div class="btn-group btn-group-sm"><a class="btn btn-outline-secondary" href="${e(m.link)}" target="_blank" rel="noopener" title="Öffnen">${icon('augen')}</a>
          <button class="btn btn-outline-secondary" data-bearbeiten="${m.id}" title="Bearbeiten">${icon('stift')}</button>
          <button class="btn btn-outline-danger" data-loeschen="${m.id}" title="Löschen">${icon('loeschen')}</button></div></td></tr>`).join('')}
    </tbody></table></div>` : `<div class="card-body py-0">${leer('Noch keine Monitore.')}</div>`}</div>`;
  const formular = (m = {}) => `
      <div class="mb-3"><label class="form-label">Name</label><input class="form-control" name="name" value="${e(m.name ?? '')}" placeholder="z. B. Foyer" required></div>
      <div class="row g-3"><div class="col-sm-8"><label class="form-label">Version</label><select class="form-select" name="version"><option value="schueler">Schülerversion</option><option value="lehrer" ${m.version === 'lehrer' ? 'selected' : ''}>Lehrerversion (Lehrerzimmer)</option></select></div>
        <div class="col-sm-4"><label class="form-label">Anzahl Tage</label><input type="number" class="form-control" name="tage" min="1" max="5" value="${m.tage ?? 2}"></div></div>`;
  const bearbeiten = m => dialog({
    titel: m ? 'Monitor bearbeiten' : 'Neuer Monitor', inhalt: formular(m),
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
      await senden(m ? `/api/app/admin/monitore/${m.id}` : '/api/app/admin/monitore', formularWerte(box), m ? 'PUT' : 'POST');
      toast('Gespeichert.'); neu();
    } }],
  });
  ziel.querySelector('[data-neu]').onclick = () => bearbeiten(null);
  ziel.querySelectorAll('[data-bearbeiten]').forEach(b => b.onclick = () => bearbeiten(liste.find(m => m.id === Number(b.dataset.bearbeiten))));
  ziel.querySelectorAll('[data-loeschen]').forEach(b => b.onclick = async () => {
    if (!await bestaetigen('Monitor löschen', 'Der Link wird ungültig. Fortfahren?')) return;
    await api(`/api/app/admin/monitore/${b.dataset.loeschen}`, { methode: 'DELETE' });
    neu();
  });
}

// ------------------------------------------------------------------ Protokoll

async function protokoll(ziel) {
  const liste = await holen('/api/app/admin/protokoll');
  ziel.innerHTML = `<div class="card">${liste.length ? `<div class="table-responsive"><table class="table table-sm table-hover table-card align-middle"><thead><tr><th>Zeit</th><th>Benutzer</th><th>Aktion</th><th>Details</th></tr></thead><tbody>
    ${liste.map(p => `<tr><td class="text-body-secondary small text-nowrap">${e(p.zeit)}</td><td>${e(p.benutzer)}</td><td>${e(p.aktion)}</td><td class="text-body-secondary small">${e(p.details)}</td></tr>`).join('')}
    </tbody></table></div>` : `<div class="card-body py-0">${leer('Noch keine Einträge.')}</div>`}</div>`;
}
