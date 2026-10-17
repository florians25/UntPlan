// Administration: Synchronisation mit UntPlan, Nachrichten zum Tag, freie Tage, Import/Export, aktive Benutzer, Rechte und Rollen,
// Benutzer, Zuordnungsgruppen, Rückmeldungen, Prüfungssperren, Ansichtseinstellungen, Monitoransichten, Protokoll.
import { e, icon, holen, senden, api, toast, dialog, bestaetigen, formularWerte, heuteIso, datumKurz, meldungText, statusMarker, leer, alarm, WOCHENTAGE } from '../ui.js';

const BEREICHE = [
  { id: 'sync', text: 'Übersicht', admin: true },
  { id: 'nachrichten', text: 'Nachrichten zum Tag', admin: false },
  { id: 'freietage', text: 'Freie Tage & Ferien', admin: true },
  { id: 'import', text: 'Import', admin: true, recht: 'admin.import' },
  { id: 'export', text: 'Export', admin: true },
  { id: 'aktive', text: 'Aktive Benutzer', admin: true, recht: 'admin.wartung' },
  { id: 'zugriff', text: 'Zugriff ent-/sperren', admin: true, recht: 'admin.wartung' },
  { id: 'ueber', text: 'Über UntPlan', admin: true, recht: 'admin.wartung' },
  { id: 'rechte', text: 'Rechte und Rollen', admin: true },
  { id: 'benutzer', text: 'Benutzer', admin: true, recht: 'admin.benutzer' },
  { id: 'personen', text: 'Personen & Konten', admin: true, recht: 'admin.benutzer' },
  { id: 'zuordnung', text: 'Zuordnungsgruppen', admin: true },
  { id: 'rueckmeldungen', text: 'Rückmeldungen', admin: true },
  { id: 'sperren', text: 'Prüfungssperren', admin: true },
  { id: 'verspaetung', text: 'Verspätungen & Fehlzeiten', admin: true },
  { id: 'ansicht', text: 'Ansichtseinstellungen', admin: true },
  { id: 'monitore', text: 'Monitoransichten', admin: true },
  { id: 'protokoll', text: 'Protokoll', admin: true },
];
const ROLLEN = { admin: 'Administration', leitung: 'Schulleitung', lehrer: 'Lehrkraft', schueler: 'Schüler*in / Klasse', eltern: 'Erziehungsberechtigte*r' };
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

export async function zeigen(seite, kontext, [bereichParam, ...weitere]) {
  const ich = kontext.ich;
  // Rechte-Modell: einzelne Bereiche auch mit dem passenden Recht (z. B. Import, Benutzerverwaltung)
  const erlaubt = BEREICHE.filter(b => ich.admin || !b.admin || (b.recht && ich.rechte?.[b.recht]));
  const bereich = erlaubt.find(b => b.id === bereichParam) ?? erlaubt[0];
  // Die Bereiche stehen im Untermenü „Administration“ der Navigation
  kontext.titel(bereich.id === 'sync' ? 'Übersicht & Synchronisation' : bereich.text);
  seite.innerHTML = '<div data-bereich></div>';
  const ziel = seite.querySelector('[data-bereich]');
  const neu = () => kontext.navigieren(location.hash);
  await ({ sync, benutzer, personen, rueckmeldungen, nachrichten, monitore, protokoll, freietage, importieren, exportieren, aktive, zugriff, ueber, rechte, zuordnung, sperren, verspaetung, ansicht })[bereich.id === 'import' ? 'importieren' : bereich.id === 'export' ? 'exportieren' : bereich.id](ziel, kontext, neu, weitere);
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
  // Version & Updates (eigene Datei)
  import('./update.js').then(m => m.einbinden(ziel, false, kontext.ich?.login)).catch(() => { /* Anzeige ist optional */ });
}

// ------------------------------------------------------------------ Benutzer und Personen (eigene Datei: benutzerverwaltung.js)

const benutzer = (...a) => import('./benutzerverwaltung.js').then(m => m.benutzer(...a));
const personen = (...a) => import('./benutzerverwaltung.js').then(m => m.personen(...a));

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
  kontext.titel('Nachrichten zum Tag', '<a class="btn btn-outline-secondary" href="#/heute">Startseite ansehen</a>');
  const liste = await holen('/api/app/admin/nachrichten');
  ziel.innerHTML = `<div class="d-flex flex-wrap align-items-center gap-2 mb-3"><p class="small text-body-secondary mb-0">Nachrichten erscheinen im Vertretungsplan sowie wahlweise auf der Übersicht und den Monitoren. Tagestexte aus UntPlan werden zusätzlich angezeigt.</p>
      <button class="btn btn-primary ms-auto" data-neu>${icon('plus', 'me-1')}Nachricht</button></div>
    <div class="card">${liste.length ? `<div class="table-responsive"><table class="table table-sm table-hover table-card align-middle"><thead><tr><th>Zeitraum</th><th>Wochentag</th><th>Betreff / Text</th><th>Für</th><th>Öffentlich</th><th>Anzeigebereich</th><th></th></tr></thead><tbody>
      ${liste.map(n => `<tr class="${n.bis < heuteIso() ? 'text-body-secondary' : ''}"><td class="text-nowrap">${e(datumKurz(n.von))}${n.bis !== n.von ? ' – ' + e(datumKurz(n.bis)) : ''}</td>
        <td class="small">${n.wochentag ? WOCHENTAGE[n.wochentag - 1] : 'jeden Tag'}</td>
        <td>${n.betreff ? `<b>${e(n.betreff)}</b><div class="small">${e(n.text)}</div>` : e(n.text)}</td><td>${e(ZIELGRUPPEN[n.zielgruppe] ?? n.zielgruppe)}</td><td>${n.oeffentlich ? 'Ja' : 'Nein'}</td>
        <td class="small">${['Vertretungsplan', n.startseite ? 'Startseite' : '', n.mobil ? 'Mobil' : '', n.monitor ? 'Monitor' : ''].filter(Boolean).join(', ')}</td>
        <td class="text-end text-nowrap"><div class="btn-group btn-group-sm"><button class="btn btn-outline-secondary" data-bearbeiten="${n.id}">${icon('stift')}</button>
          <button class="btn btn-outline-danger" data-loeschen="${n.id}">${icon('loeschen')}</button></div></td></tr>`).join('')}
    </tbody></table></div>` : `<div class="card-body py-0">${leer('Keine Nachrichten.')}</div>`}</div>`;
  const formular = (n = {}) => `
      <div class="row g-3 mb-3">
        <div class="col-sm-4"><label class="form-label">Von</label><input type="date" class="form-control" name="von" value="${e(n.von ?? heuteIso())}" required></div>
        <div class="col-sm-4"><label class="form-label">Bis</label><input type="date" class="form-control" name="bis" value="${e(n.bis ?? heuteIso())}" required></div>
        <div class="col-sm-4"><label class="form-label">Für</label><select class="form-select" name="zielgruppe">${Object.entries(ZIELGRUPPEN).map(([z, t]) => `<option value="${z}" ${z === (n.zielgruppe ?? 'alle') ? 'selected' : ''}>${t}</option>`).join('')}</select></div>
      </div>
      <div class="row g-3 mb-3">
        <div class="col-sm-6"><label class="form-label">Wochentag</label><select class="form-select" name="wochentag"><option value="0">jeden Tag</option>${WOCHENTAGE.map((w, i) => `<option value="${i + 1}" ${n.wochentag === i + 1 ? 'selected' : ''}>${w}</option>`).join('')}</select></div>
        <div class="col-sm-6"><label class="form-label">Reihenfolge</label><input type="number" class="form-control" name="reihenfolge" value="${n.reihenfolge ?? 10}"></div>
      </div>
      <div class="mb-3"><label class="form-label">Betreff <span class="text-body-secondary fw-normal">(optional)</span></label><input class="form-control" name="betreff" value="${e(n.betreff ?? '')}" maxlength="200"></div>
      <div class="mb-3"><label class="form-label">Text</label><textarea class="form-control" name="text" rows="4" required>${e(n.text ?? '')}</textarea></div>
      <div class="row"><div class="col-sm-6">
        <div class="form-check"><input class="form-check-input" type="checkbox" name="aufgefaechert" id="n-a" ${n.aufgefaechert ?? true ? 'checked' : ''}><label class="form-check-label" for="n-a">Aufgefächert anzeigen</label></div>
        <div class="form-check"><input class="form-check-input" type="checkbox" name="oeffentlich" id="n-o" ${n.oeffentlich ? 'checked' : ''}><label class="form-check-label" for="n-o">Öffentlich (RSS-Feed)</label></div></div>
      <div class="col-sm-6"><div class="small fw-bold mb-1">Anzeigebereich</div>
        <div class="form-check"><input class="form-check-input" type="checkbox" name="startseite" id="n-s" ${n.startseite ?? true ? 'checked' : ''}><label class="form-check-label" for="n-s">Startseite „Heute“</label></div>
        <div class="form-check"><input class="form-check-input" type="checkbox" name="mobil" id="n-mo" ${n.mobil ?? true ? 'checked' : ''}><label class="form-check-label" for="n-mo">UntPlanMobile</label></div>
        <div class="form-check"><input class="form-check-input" type="checkbox" name="monitor" id="n-m" ${n.monitor ?? true ? 'checked' : ''}><label class="form-check-label" for="n-m">Monitore</label></div></div></div>`;
  const bearbeiten = n => dialog({
    titel: n ? 'Nachricht bearbeiten' : 'Neue Nachricht', inhalt: formular(n ?? {}),
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
      const w = formularWerte(box);
      await senden(n ? `/api/app/admin/nachrichten/${n.id}` : '/api/app/admin/nachrichten', { ...w, wochentag: Number(w.wochentag), reihenfolge: Number(w.reihenfolge) || 10 }, n ? 'PUT' : 'POST');
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
  const [liste, anzeige] = await Promise.all([holen('/api/app/admin/monitore'), holen('/api/app/admin/einstellungen/monitor')]);
  ziel.innerHTML = `<div class="card mb-3 u-einstellungen"><div class="card-header">Darstellung</div><div class="card-body"><form data-anzeige>
      ${schalter('ticker', 'Nachrichten zum Tag als Laufschrift (Ticker)', anzeige)}${schalter('kopfzeile', 'Nachrichten zum Tag in der Kopfzeile', anzeige)}
      ${schalter('nurOeffentlich', 'Nur öffentliche Nachrichten anzeigen', anzeige)}
      <div class="row g-3 mt-1"><div class="col-sm-5"><label class="form-label">Aktualisierung [Sekunden]</label><input type="number" min="15" max="3600" class="form-control" name="aktualisierung" value="${anzeige.aktualisierung}"></div></div>
      <button class="btn btn-primary mt-3" type="submit">Speichern</button></form></div></div>
    <div class="d-flex flex-wrap align-items-center gap-2 mb-3"><p class="small text-body-secondary mb-0">Ein Monitor zeigt den Vertretungsplan ohne Anmeldung über einen geheimen Link an, z. B. im Eingangsbereich (Browser im Vollbild).</p>
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
    titel: m ? 'Monitor bearbeiten' : 'Neuer Monitor', inhalt: formular(m ?? {}),
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
      await senden(m ? `/api/app/admin/monitore/${m.id}` : '/api/app/admin/monitore', formularWerte(box), m ? 'PUT' : 'POST');
      toast('Gespeichert.'); neu();
    } }],
  });
  einstellungBinden(ziel.querySelector('[data-anzeige]'), 'monitor');
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

// ------------------------------------------------------------------ Einstellungen (gemeinsame Bausteine)

/** Schalter (Kontrollkästchen) für eine Einstellung. */
const schalter = (name, text, werte) => `<div class="form-check form-switch"><input class="form-check-input" type="checkbox" role="switch" name="${name}" id="es-${name}" ${werte[name] ? 'checked' : ''}>
  <label class="form-check-label" for="es-${name}">${e(text)}</label></div>`;

/** Formular einer Einstellung speichern (Kontrollkästchen als bool, Zahlen als Zahl). */
function einstellungBinden(form, bereich, nachher) {
  form.onsubmit = async ev => {
    ev.preventDefault();
    const w = {};
    form.querySelectorAll('[name]').forEach(f => { w[f.name] = f.type === 'checkbox' ? f.checked : f.type === 'number' ? Number(f.value) : f.value; });
    try { await senden(`/api/app/admin/einstellungen/${bereich}`, w, 'PUT'); toast('Einstellungen gespeichert.'); nachher?.(); }
    catch (err) { toast(err.message, true); }
  };
}

// ------------------------------------------------------------------ Freie Tage & Ferien

const FREIART = { frei: 'Unterrichtsfrei', ferien: 'Ferien', feiertag: 'Feiertag' };

async function freietage(ziel, kontext, neu) {
  const d = await holen('/api/app/admin/freietage');
  kontext.titel('Freie Tage & Ferien', `<button class="btn btn-primary" data-neu>${icon('plus')}Neu</button>`);
  const datum = s => s ? s.split('-').reverse().join('.') : '';
  ziel.innerHTML = `<p class="small text-body-secondary">Ferien und Feiertage kommen aus UntPlan. Hier lassen sich zusätzliche unterrichtsfreie Tage eintragen (z. B. pädagogischer Tag, beweglicher Ferientag) –
      sie erscheinen sofort in allen Plänen, im Vertretungsplan und im Klassenbuch.</p>
    <div class="card mb-3"><div class="card-header">In UntPlanWeb eingetragen</div>${d.eigene.length ? `<div class="table-responsive"><table class="table table-sm table-hover table-card align-middle"><thead><tr><th>Bezeichnung</th><th>Art</th><th>Von</th><th>Bis</th><th></th></tr></thead><tbody>
      ${d.eigene.map(f => `<tr><td class="fw-semibold">${e(f.name)}</td><td>${FREIART[f.art] ?? e(f.art)}</td><td>${datum(f.von)}</td><td>${datum(f.bis)}</td>
        <td class="text-end"><button class="btn btn-sm btn-outline-secondary" data-bearbeiten="${f.id}">${icon('stift')}</button> <button class="btn btn-sm btn-outline-danger" data-loeschen="${f.id}">${icon('loeschen')}</button></td></tr>`).join('')}
      </tbody></table></div>` : `<div class="card-body py-0">${leer('Keine zusätzlichen freien Tage.', 'calendar-x')}</div>`}</div>
    <div class="card"><div class="card-header">Aus UntPlan übertragen</div>${d.ausUntPlan.length ? `<div class="table-responsive"><table class="table table-sm table-card align-middle"><thead><tr><th>Bezeichnung</th><th>Art</th><th>Von</th><th>Bis</th></tr></thead><tbody>
      ${d.ausUntPlan.map(f => `<tr><td>${e(f.name)}</td><td>${f.feiertag ? 'Feiertag' : 'Ferien'}</td><td>${datum(f.von)}</td><td>${datum(f.bis)}</td></tr>`).join('')}</tbody></table></div>`
      : `<div class="card-body py-0">${leer('Keine Ferien übertragen.')}</div>`}</div>`;
  const formular = f => dialog({
    titel: f ? 'Freien Tag bearbeiten' : 'Freien Tag eintragen',
    inhalt: `<div class="row g-3"><div class="col-12"><label class="form-label">Bezeichnung</label><input class="form-control" name="name" value="${e(f?.name ?? '')}" placeholder="z. B. Pädagogischer Tag" required></div>
      <div class="col-sm-4"><label class="form-label">Art</label><select class="form-select" name="art">${Object.entries(FREIART).map(([k, t]) => `<option value="${k}" ${k === (f?.art ?? 'frei') ? 'selected' : ''}>${t}</option>`).join('')}</select></div>
      <div class="col-sm-4"><label class="form-label">Von</label><input type="date" class="form-control" name="von" value="${f?.von ?? heuteIso()}"></div>
      <div class="col-sm-4"><label class="form-label">Bis</label><input type="date" class="form-control" name="bis" value="${f?.bis ?? heuteIso()}"></div></div>`,
    knoepfe: [{ text: 'Abbrechen' }, { text: f ? 'Speichern' : 'Erstellen', art: 'haupt', aktion: async box => {
      await senden(f ? `/api/app/admin/freietage/${f.id}` : '/api/app/admin/freietage', formularWerte(box), f ? 'PUT' : 'POST');
      toast('Gespeichert.'); neu();
    } }],
  });
  document.querySelector('[data-neu]').onclick = () => formular(null);
  ziel.querySelectorAll('[data-bearbeiten]').forEach(b => b.onclick = () => formular(d.eigene.find(f => f.id === Number(b.dataset.bearbeiten))));
  ziel.querySelectorAll('[data-loeschen]').forEach(b => b.onclick = async () => {
    if (!await bestaetigen('Freien Tag löschen', 'Diesen Eintrag löschen?')) return;
    await api(`/api/app/admin/freietage/${b.dataset.loeschen}`, { methode: 'DELETE' });
    neu();
  });
}

// ------------------------------------------------------------------ Import (Benutzer aus CSV)

async function importieren(ziel, kontext, neu, weitere = []) {
  // Reiter: Benutzer (CSV, unten) und Schüler*innen (import.js)
  const reiter = weitere[0] === 'schueler' ? 'schueler' : 'benutzer';
  const nav = `<ul class="nav nav-tabs mb-3"><li class="nav-item"><a class="nav-link ${reiter === 'benutzer' ? 'active' : ''}" href="#/admin/import">Benutzer</a></li>
    <li class="nav-item"><a class="nav-link ${reiter === 'schueler' ? 'active' : ''}" href="#/admin/import/schueler">Schüler*innen</a></li></ul>`;
  if (reiter === 'schueler') {
    ziel.innerHTML = nav + '<div data-schuelerimport></div>';
    return (await import('./import.js')).zeigen(ziel.querySelector('[data-schuelerimport]'), kontext);
  }
  ziel.innerHTML = nav + `<div class="card u-einstellungen mb-3"><div class="card-header">Benutzer importieren (CSV)</div><div class="card-body">
      <p class="small text-body-secondary">Spalten: <code>Benutzername;Name;Rolle;Kürzel;E-Mail;Passwort</code> (Semikolon oder Komma, UTF-8 oder Windows-1252, erste Zeile darf eine Kopfzeile sein).
        Rolle: Administration, Schulleitung, Lehrkraft oder Klasse. Ohne Passwort wird ein Startpasswort erzeugt, das bei der ersten Anmeldung geändert werden muss.</p>
      <div class="d-flex flex-wrap gap-2 align-items-center"><label class="btn btn-outline-secondary mb-0">${icon('hochladen')}Datei wählen und prüfen<input type="file" accept=".csv,text/csv,text/plain" data-datei hidden></label>
        <div class="form-check ms-2"><input class="form-check-input" type="checkbox" id="imp-ers" data-ersetzen><label class="form-check-label" for="imp-ers">Vorhandene Konten aktualisieren (Ersetzen)</label></div></div>
    </div></div><div data-vorschau></div>
    <p class="small text-body-secondary">Stammdaten (Lehrkräfte, Fächer, Klassen, Räume) und Pausenaufsichten werden nicht hier importiert: sie kommen bei jeder Übertragung aus UntPlan.</p>`;
  const vorschau = ziel.querySelector('[data-vorschau]');
  ziel.querySelector('[data-datei]').onchange = async ev => {
    const datei = ev.target.files[0];
    if (!datei) return;
    const fd = new FormData();
    fd.append('datei', datei);
    try {
      const r = await api('/api/app/admin/import/pruefen', { methode: 'POST', formular: fd });
      vorschau.innerHTML = `<div class="card mb-3"><div class="card-header d-flex flex-wrap gap-2 align-items-center">Vorschau
          <span class="u-chip gruen">${r.neu} neu</span><span class="u-chip">${r.vorhanden} vorhanden</span>${r.fehler ? `<span class="u-chip rot">${r.fehler} Fehler</span>` : ''}
          <button class="btn btn-primary btn-sm ms-auto" data-uebernehmen ${r.fehler ? 'disabled title="Bitte zuerst die Fehler in der Datei beheben"' : ''}>Übernehmen</button></div>
        <div class="table-responsive" style="max-height:420px"><table class="table table-sm table-card align-middle"><thead><tr><th>Zeile</th><th>Benutzername</th><th>Name</th><th>Rolle</th><th>Kürzel</th><th>E-Mail</th><th>Hinweis</th></tr></thead><tbody>
        ${r.zeilen.slice(0, 500).map(z => `<tr class="${z.fehler ? 'u-import-fehler' : ''}"><td>${z.zeile}</td><td class="fw-semibold">${e(z.login)}</td><td>${e(z.anzeigename)}</td><td>${e(ROLLEN[z.rolle] ?? z.rolle)}</td><td>${e(z.element)}</td><td>${e(z.email)}</td>
          <td class="small">${z.fehler ? e(z.fehler) : z.vorhanden ? 'vorhanden' : 'neu'}</td></tr>`).join('')}</tbody></table></div></div>`;
      vorschau.querySelector('[data-uebernehmen]').onclick = async () => {
        const ersetzen = ziel.querySelector('[data-ersetzen]').checked;
        if (ersetzen && !await bestaetigen('Konten aktualisieren', 'Vorhandene Konten werden mit Name, Rolle, Kürzel und E-Mail aus der Datei überschrieben. Fortfahren?', 'Übernehmen')) return;
        const e2 = await senden('/api/app/admin/import/uebernehmen', { zeilen: r.zeilen, ersetzen });
        dialog({ titel: 'Import abgeschlossen', breit: true, inhalt: `<p>${e2.neu.length} Konten angelegt, ${e2.aktualisiert} aktualisiert, ${e2.uebersprungen} übersprungen.</p>
          ${e2.neu.length ? alarm('warning', 'Die Startpasswörter werden nur jetzt angezeigt. Bitte die Liste herunterladen.') : ''}`,
          knoepfe: e2.neu.length ? [{ text: 'Liste herunterladen (CSV)', aktion: () => { herunterladen(`import-${heuteIso()}.csv`, e2.csv); return false; } }, { text: 'Schließen', art: 'haupt' }] : [{ text: 'OK', art: 'haupt' }] });
        vorschau.innerHTML = '';
      };
    } catch (err) { kontext.fehler(err); }
    ev.target.value = '';
  };
}

// ------------------------------------------------------------------ Export

async function exportieren(ziel) {
  const csv = zeilen => zeilen.map(z => z.map(x => `"${String(x ?? '').replace(/"/g, '""')}"`).join(';')).join('\r\n');
  ziel.innerHTML = `<div class="u-raster-2">
    ${[['benutzer', 'Benutzer', 'Alle Benutzerkonten mit Rolle, Kürzel, E-Mail und letzter Anmeldung.'], ['rollen', 'Rechte und Rollen', 'Alle Rollen (Rechte-Profile) mit Basisrolle, Anzahl der Benutzer und Stufe je Recht.'],
      ['nachrichten', 'Nachrichten zum Tag', 'Alle Nachrichten mit Zeitraum, Betreff und Anzeigebereich.'], ['rueckmeldungen', 'Rückmeldungen', 'Meldungen an UntPlan (Abwesenheiten, Buchungen, Klausuren).'],
      ['protokoll', 'Protokoll', 'Die letzten 300 Einträge des Protokolls.']].map(([id, t, x]) => `<div class="card"><div class="card-body">
      <h2 class="h6 fw-bold">${t}</h2><p class="small text-body-secondary">${x}</p><button class="btn btn-outline-secondary btn-sm" data-export="${id}">${icon('herunterladen')}CSV herunterladen</button></div></div>`).join('')}</div>`;
  ziel.querySelectorAll('[data-export]').forEach(b => b.onclick = async () => {
    const id = b.dataset.export;
    let zeilen;
    if (id === 'benutzer') zeilen = [['Benutzername', 'Name', 'Rolle', 'Kürzel', 'E-Mail', 'gesperrt', 'erstellt', 'Letzte Anmeldung'], ...(await holen('/api/app/admin/benutzer')).map(u => [u.login, u.anzeigename, u.rollenname, u.element, u.email, u.gesperrt ? 'ja' : 'nein', u.erstellt, u.letzteAnmeldung ?? ''])];
    else if (id === 'rollen') {
      // Rechte-Modell: je Rolle (Rechte-Profil) Basisrolle, Anzahl Konten und Stufe je Recht
      const k = await holen('/api/app/admin/rechte/katalog');
      const stufe = (r, s) => s === 0 ? 'Nein' : s === 1 ? 'Lesen' : r.zweistufig ? 'Schreiben' : 'Ja';
      zeilen = [['Rolle', 'Art', 'Basisrolle', 'Benutzer', 'Beschreibung', ...k.rechte.map(r => `${r.gruppe}: ${r.name}`)],
        ...k.profile.map(p => [p.name, p.system ? 'Systemrolle' : 'eigene Rolle', p.basisName, p.benutzer, p.beschreibung, ...k.rechte.map(r => stufe(r, p.rechte[r.id] ?? 0))])];
    }
    else if (id === 'nachrichten') zeilen = [['Von', 'Bis', 'Wochentag', 'Betreff', 'Text', 'Für', 'Öffentlich', 'Startseite', 'Monitor'], ...(await holen('/api/app/admin/nachrichten')).map(n => [n.von, n.bis, n.wochentag ? WOCHENTAGE[n.wochentag - 1] : 'jeden Tag', n.betreff, n.text, n.zielgruppe, n.oeffentlich ? 'ja' : 'nein', n.startseite ? 'ja' : 'nein', n.monitor ? 'ja' : 'nein'])];
    else if (id === 'rueckmeldungen') zeilen = [['Meldung', 'Von', 'Erstellt', 'Status'], ...(await holen('/api/app/admin/rueckmeldungen')).map(m => [meldungText(m), m.absender, m.erstellt, m.status])];
    else zeilen = [['Zeit', 'Benutzer', 'Aktion', 'Details'], ...(await holen('/api/app/admin/protokoll')).map(p => [p.zeit, p.benutzer, p.aktion, p.details])];
    herunterladen(`${id}-${heuteIso()}.csv`, csv(zeilen));
  });
}

// ------------------------------------------------------------------ Aktive Benutzer

// Wartung (wartung.js): aktive Sitzungen, Zugriff ent-/sperren, Über UntPlan
async function aktive(ziel, kontext, neu, weitere = []) { return (await import('./wartung.js')).aktive(ziel, kontext, neu, weitere); }
async function zugriff(ziel, kontext, neu) { return (await import('./wartung.js')).zugriff(ziel, kontext, neu); }
async function ueber(ziel) { return (await import('./wartung.js')).ueber(ziel); }

// ------------------------------------------------------------------ Rechte und Rollen


async function rechte(ziel, kontext, neu, weitere = []) {
  // Rechte-Modell (rechte.js): Rollen × Rechte, Rechte einzelner Benutzer, Passwortregeln
  return (await import('./rechte.js')).zeigen(ziel, kontext, weitere[0] || 'rollen');
}

// ------------------------------------------------------------------ Zuordnungsgruppen, Prüfungssperren, Ansicht

async function zuordnung(ziel) {
  const w = await holen('/api/app/admin/einstellungen/zuordnung');
  ziel.innerHTML = `<div class="card u-einstellungen"><div class="card-header">Zuordnung beim Anlegen von Konten aus den Stammdaten</div><div class="card-body"><form>
    <p class="small text-body-secondary">Bei „Konten aus Stammdaten“ (Benutzer) werden Lehrkräfte der Rolle Lehrkraft und Klassen der Rolle Klasse zugeordnet.</p>
    <div class="mb-3"><label class="form-label">Kürzel, die der Schulleitung zugeordnet werden</label><input class="form-control" name="leitungKuerzel" value="${e(w.leitungKuerzel)}" placeholder="z. B. MUE, SCH"><div class="form-text">Mehrere Kürzel durch Komma trennen.</div></div>
    ${schalter('klassenkonten', 'Klassenkonten standardmäßig mit anlegen', w)}
    <button class="btn btn-primary mt-3" type="submit">Speichern</button></form></div></div>`;
  einstellungBinden(ziel.querySelector('form'), 'zuordnung');
}

async function sperren(ziel) {
  const w = await holen('/api/app/admin/einstellungen/pruefungssperren');
  ziel.innerHTML = `<div class="card u-einstellungen"><div class="card-header">Zeitliche Einschränkungen für das Eintragen von Prüfungen</div><div class="card-body"><form>
    ${schalter('aktiv', 'Prüfungssperren aktivieren', w)}
    <div class="row g-3 my-1"><div class="col-sm-6"><label class="form-label">Höchstens Prüfungen je Klasse und Tag</label><input type="number" class="form-control" name="maxProTag" min="0" max="10" value="${w.maxProTag}"></div>
      <div class="col-sm-6"><label class="form-label">Höchstens Prüfungen je Klasse und Woche</label><input type="number" class="form-control" name="maxProWoche" min="0" max="20" value="${w.maxProWoche}"></div></div>
    ${schalter('leitungAusnehmen', 'Schulleitung und Administration von den Sperren ausnehmen', w)}
    <p class="small text-body-secondary mt-2 mb-3">Gezählt werden Klausuren aus UntPlan und noch nicht übernommene Prüfungen aus UntPlanWeb. 0 = keine Grenze.</p>
    <button class="btn btn-primary" type="submit">Speichern</button></form></div></div>`;
  einstellungBinden(ziel.querySelector('form'), 'pruefungssperren');
}

async function verspaetung(ziel) {
  const w = await holen('/api/app/admin/einstellungen/verspaetung');
  ziel.innerHTML = `<div class="card u-einstellungen"><div class="card-header">Verspätungen und vorzeitiges Gehen</div><div class="card-body"><form>
    <div class="row g-3 mb-2">
      <div class="col-sm-6"><label class="form-label">Stunde gilt als Fehlstunde ab</label><div class="input-group"><input type="number" class="form-control" name="abwesendAbMinuten" min="0" max="240" value="${w.abwesendAbMinuten}"><span class="input-group-text">Minuten</span></div>
        <div class="form-text">0 = nur ganz verpasste Stunden werden Fehlstunden. Beispiel 20: Wer 20 min oder mehr zu spät kommt, fehlt in dieser Stunde.</div></div>
      <div class="col-sm-6"><label class="form-label">Hinweis an die Klassenleitung ab</label><div class="input-group"><input type="number" class="form-control" name="hinweisAbAnzahl" min="0" max="100" value="${w.hinweisAbAnzahl}"><span class="input-group-text">Verspätungen</span></div>
        <div class="form-text">Im Fehlzeitenbericht und in der Klassenübersicht markiert; 0 = aus.</div></div>
    </div>
    ${schalter('minutenUmrechnen', 'Verspätungs- und Gehminuten zu Fehlstunden zusammenrechnen', w)}
    <div class="row g-3 my-1"><div class="col-sm-6"><label class="form-label">Minuten je Fehlstunde</label><input type="number" class="form-control" name="minutenProFehlstunde" min="1" max="240" value="${w.minutenProFehlstunde}"></div></div>
    ${schalter('vorzeitigRestTag', 'Beim vorzeitigen Gehen standardmäßig für den Rest des Tages abwesend eintragen', w)}
    <p class="small text-body-secondary mt-2 mb-3">Verspätungen werden ab der ersten, vorzeitiges Gehen bis zur letzten Stunde eines Blocks gerechnet; ganz verpasste Stunden zählen immer als Fehlstunden.
      Der Entschuldigungsstatus bleibt beim Bearbeiten erhalten.</p>
    <button class="btn btn-primary" type="submit">Speichern</button></form></div></div>`;
  einstellungBinden(ziel.querySelector('form'), 'verspaetung');
}

async function ansicht(ziel, kontext) {
  const w = await holen('/api/app/admin/einstellungen/ansicht');
  ziel.innerHTML = `<div class="card u-einstellungen"><div class="card-header">Globale Einstellungen der Oberfläche</div><div class="card-body"><form>
    ${schalter('nachrichtenAnzeigen', 'Nachrichten zum Tag auf der Startseite anzeigen', w)}
    ${schalter('rss', 'Öffentliche Nachrichten zum Tag als RSS-Feed veröffentlichen (/rss/nachrichten.xml)', w)}
    ${schalter('kartenZeiten', 'Uhrzeiten auf den Stundenkarten anzeigen (Voreinstellung)', w)}
    <div class="row g-3 my-1"><div class="col-sm-6"><label class="form-label">Startseite nach der Anmeldung</label><select class="form-select" name="startseite">
      <option value="heute" ${w.startseite === 'heute' ? 'selected' : ''}>Heute</option><option value="stundenplan" ${w.startseite === 'stundenplan' ? 'selected' : ''}>Mein Stundenplan</option></select></div></div>
    <button class="btn btn-primary mt-2" type="submit">Speichern</button></form></div></div>
    <div class="card u-einstellungen mt-3"><div class="card-header">Schrift</div><div class="card-body" data-schriften></div></div><div class="mt-3" data-schuelerquelle></div>`;
  einstellungBinden(ziel.querySelector('form'), 'ansicht', () => kontext.sitzungNeu());
  import('./import.js').then(m => m.quelleKarte(ziel.querySelector('[data-schuelerquelle]'))).catch(() => { /* ohne Schülerquelle */ });
  schriften(ziel.querySelector('[data-schriften]'));
}

/** Hausschrift (z. B. AvenirNextPro): lizenzierte Schriftdateien hochladen; sie liegen nur im Datenordner dieses Servers. */
async function schriften(box) {
  const liste = await holen('/api/app/admin/schriften');
  const schnitt = x => `${({ 200: 'Ultra Light', 300: 'Light', 400: 'Regular', 500: 'Medium', 600: 'Demi', 700: 'Bold', 800: 'Heavy' })[x.gewicht] ?? x.gewicht}${x.kursiv ? ' Italic' : ''}`;
  box.innerHTML = `<p class="small text-body-secondary">Die Oberfläche verwendet die Schrift <b>AvenirNextPro</b>, sobald die Schriftdateien hier hochgeladen sind (.otf, .ttf, .woff oder .woff2, Schnitt aus dem Dateinamen, z. B. „…-Regular“, „…-Medium“). Verwendet werden nur Regular und Medium; Demi/Bold nur ersatzweise, wenn Medium fehlt.
      Die Dateien werden nicht mit UntPlanWeb ausgeliefert – bitte nur Schriften mit gültiger Web-Lizenz verwenden. Ohne Dateien gilt die freie Ersatzschrift Nunito Sans.</p>
    ${liste.length ? `<table class="table table-sm align-middle"><thead><tr><th>Datei</th><th>Schnitt</th><th class="text-end">Größe</th><th></th></tr></thead><tbody>
      ${liste.map(x => `<tr><td>${e(x.datei)}</td><td style="font-family:AvenirNextPro;font-weight:${x.cssGewicht ?? x.gewicht};font-style:${x.kursiv ? 'italic' : 'normal'}">${schnitt(x)}</td><td class="text-end small">${Math.round(x.groesse / 1024)} KB</td>
        <td class="text-end"><button class="btn btn-sm btn-outline-danger" data-schrift-weg="${e(x.datei)}" title="Löschen">${icon('loeschen')}</button></td></tr>`).join('')}</tbody></table>` : '<p class="small">Noch keine Schriftdateien hochgeladen.</p>'}
    <label class="btn btn-outline-secondary btn-sm">${icon('plus')}Schriftdateien hochladen<input type="file" class="d-none" accept=".otf,.ttf,.woff,.woff2" multiple data-schrift-datei></label>`;
  box.querySelector('[data-schrift-datei]').onchange = async ev => {
    const fd = new FormData();
    [...ev.target.files].forEach(f => fd.append('dateien', f));
    try {
      const r = await fetch('/api/app/admin/schriften', { method: 'POST', body: fd, headers: { 'X-UntPlanWeb': '1' } });
      if (!r.ok) throw new Error((await r.json().catch(() => ({}))).fehler || 'Hochladen fehlgeschlagen.');
      toast('Schrift hochgeladen – die Seite wird neu geladen.'); setTimeout(() => location.reload(), 600);
    } catch (err) { toast(err.message, true); }
  };
  box.querySelectorAll('[data-schrift-weg]').forEach(b => b.onclick = async () => {
    try { await senden(`/api/app/admin/schriften/${encodeURIComponent(b.dataset.schriftWeg)}`, null, 'DELETE'); schriften(box); } catch (err) { toast(err.message, true); }
  });
}
