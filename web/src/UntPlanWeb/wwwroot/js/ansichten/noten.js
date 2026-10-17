// Stammdaten | Notenschemen und Prüfungsarten (Recht „Stammdaten“) im modernen Design.
// Notenschemen: Gesamtnotenschema, Liste (Auswahl, Name, Noten, aktiv) mit Neu/Sortieren/Löschen; Bearbeiten im Fenster mit Name, aktiv und
// den Noten (Note und optionaler Wert, Reihenfolge hoch/runter, hinzufügen/löschen) – ohne Werte keine Durchschnitte.
// Prüfungsarten: Suche, Liste mit Höchstzahlen, Gruppierung, Notenschema und aktiv; Formular „Neue Prüfungsart“ mit Speichern, Speichern & Neu, Abbrechen.
import { e, icon, holen, senden, toast, dialog, bestaetigen, herunterladen } from '../ui.js';
import { cssLaden } from './kbeinstellungen.js';

export async function zeigen(seite, kontext, art) {
  cssLaden();
  return art === 'notenschemen' ? notenschemen(seite, kontext) : pruefungsarten(seite, kontext);
}

const neuLaden = kontext => kontext.navigieren(location.hash);
const csvZelle = x => `"${String(x ?? '').replace(/"/g, '""')}"`;

/** Auswahl-Fußzeile mit Anzahl und Knöpfen wie in der Vorlage (Neu, Sortieren, Löschen …). */
const fuss = (n, knoepfe) => `<div class="nl-fuss"><span class="kbe-anzahl">${n} ${n === 1 ? 'Element' : 'Elemente'}</span><span class="ms-auto d-flex flex-wrap gap-2">${knoepfe}</span></div>`;

/** Reihenfolge per Pfeil ändern und speichern. */
async function verschieben(pfad, liste, i, richtung, kontext) {
  const ids = liste.map(x => x.id);
  const j = i + richtung;
  if (j < 0 || j >= ids.length) return;
  [ids[i], ids[j]] = [ids[j], ids[i]];
  await senden(`${pfad}/reihenfolge`, { ids }, 'PUT');
  neuLaden(kontext);
}

function auswahlBinden(seite, aktualisieren) {
  const alle = seite.querySelector('[data-alle]');
  const boxen = () => [...seite.querySelectorAll('[data-wahl]')];
  const lesen = () => { const ids = boxen().filter(b => b.checked).map(b => Number(b.dataset.wahl)); aktualisieren(ids); return ids; };
  boxen().forEach(b => b.onchange = () => { b.closest('tr').classList.toggle('nl-gewaehlt', b.checked); if (alle) alle.checked = boxen().every(x => x.checked); lesen(); });
  if (alle) alle.onchange = () => { boxen().forEach(b => { b.checked = alle.checked; b.closest('tr').classList.toggle('nl-gewaehlt', b.checked); }); lesen(); };
  return lesen;
}

// ------------------------------------------------------------------ Notenschemen

async function notenschemen(seite, kontext) {
  const d = await holen('/api/app/notenschemen');
  const liste = d.schemen;
  kontext.titel('Notenschemen', `<button class="btn btn-primary" data-neu>${icon('plus')}Neu</button>`, String(liste.length));
  seite.innerHTML = `<div class="kbe">
    <p class="nl-hinweis"><i class="bi bi-info-circle"></i>Notenschemen legen fest, welche Noten vergeben werden, z. B. „1 – 5“, „+, ~, -“ oder „Bestanden / Nicht bestanden“.
      Werte sind optional – ohne Werte werden für das Schema keine Durchschnittsnoten berechnet.</p>
    <section class="kbe-karte"><header><span class="kbe-symbol"><i class="bi bi-trophy" aria-hidden="true"></i></span><h2>Gesamtnotenschema</h2></header>
      <div class="kbe-rumpf"><div class="kbe-zeile"><div class="kbe-label"><label for="ns-gesamt">Schema für Gesamtnoten</label></div><div class="kbe-wert"><span class="kbe-reihe">
        <select class="form-select kbe-auswahl" id="ns-gesamt" data-gesamt><option value="">– Auswahl –</option>${liste.filter(s => s.aktiv).map(s => `<option value="${s.id}" ${s.id === d.gesamt ? 'selected' : ''}>${e(s.name)}</option>`).join('')}</select>
        <button type="button" class="nl-knopf" data-gesamt-speichern>Speichern</button></span></div></div></div></section>
    <div class="nl-tabelle kbe-tabelle">${liste.length ? `<table><thead><tr>
        <th class="nl-wahl"><input type="checkbox" class="nl-rund" data-alle aria-label="Alle auswählen"></th><th>Name</th><th>Noten</th><th>Durchschnitt</th><th class="text-center">aktiv</th><th class="text-end"><span class="visually-hidden">Aktionen</span></th></tr></thead>
      <tbody>${liste.map((s, i) => `<tr>
        <td class="nl-wahl"><input type="checkbox" class="nl-rund" data-wahl="${s.id}" aria-label="${e(s.name)} auswählen"></td>
        <td class="kbe-name"><button type="button" class="kbe-link" data-bearbeiten="${i}">${e(s.name)}</button></td>
        <td><span class="kbe-noten">${s.noten.map(n => `<span class="kbe-note">${e(n.text)}${n.wert !== null && n.wert !== undefined ? `<small>${e(String(n.wert).replace('.', ','))}</small>` : ''}</span>`).join('')}</span></td>
        <td>${s.mitDurchschnitt ? '<span class="nl-chip gruen">ja</span>' : '<span class="nl-chip grau">ohne Werte</span>'}</td>
        <td class="text-center">${s.aktiv ? '<i class="bi bi-check-lg kbe-ja" aria-label="aktiv"></i>' : '<span class="visually-hidden">inaktiv</span>'}</td>
        <td class="text-end text-nowrap">
          <button type="button" class="kbe-iknopf" data-hoch="${i}" title="Nach oben" aria-label="Nach oben" ${i === 0 ? 'disabled' : ''}><i class="bi bi-arrow-up"></i></button>
          <button type="button" class="kbe-iknopf" data-runter="${i}" title="Nach unten" aria-label="Nach unten" ${i === liste.length - 1 ? 'disabled' : ''}><i class="bi bi-arrow-down"></i></button>
          <button type="button" class="kbe-iknopf" data-bearbeiten="${i}" title="Bearbeiten" aria-label="${e(s.name)} bearbeiten"><i class="bi bi-pencil"></i></button>
          <button type="button" class="kbe-iknopf rot" data-loeschen="${s.id}" title="Löschen" aria-label="${e(s.name)} löschen"><i class="bi bi-trash3"></i></button></td></tr>`).join('')}</tbody></table>` :
      '<div class="s-leer"><span class="s-leer-symbol"><i class="bi bi-award"></i></span><b>Noch keine Notenschemen</b><p>Legen Sie mit „Neu“ das erste Notenschema an.</p></div>'}
      ${fuss(liste.length, `<button type="button" class="nl-knopf" data-neu2>${icon('plus')}Neu</button>
        <button type="button" class="nl-knopf" data-sortieren ${liste.length > 1 ? '' : 'disabled'} title="Alphabetisch sortieren"><i class="bi bi-sort-alpha-down"></i> Sortieren</button>
        <button type="button" class="nl-knopf rot" data-auswahl-loeschen disabled><i class="bi bi-trash3"></i> Löschen</button>`)}</div></div>`;

  const auswahl = auswahlBinden(seite, ids => { seite.querySelector('[data-auswahl-loeschen]').disabled = !ids.length; });
  const loeschen = async ids => {
    const namen = liste.filter(s => ids.includes(s.id)).map(s => s.name);
    if (!ids.length || !await bestaetigen('Notenschemen löschen', `${namen.length === 1 ? `„${namen[0]}“` : `${namen.length} Notenschemen`} löschen? Prüfungsarten mit diesem Schema verlieren die Zuordnung.`)) return;
    await senden('/api/app/notenschemen/loeschen', { ids });
    toast('Gelöscht.');
    neuLaden(kontext);
  };
  document.querySelector('[data-neu]').onclick = () => schemaFormular(null, kontext);
  seite.querySelector('[data-neu2]').onclick = () => schemaFormular(null, kontext);
  seite.querySelectorAll('[data-bearbeiten]').forEach(b => b.onclick = () => schemaFormular(liste[Number(b.dataset.bearbeiten)], kontext));
  seite.querySelectorAll('[data-loeschen]').forEach(b => b.onclick = () => loeschen([Number(b.dataset.loeschen)]));
  seite.querySelector('[data-auswahl-loeschen]').onclick = () => loeschen(auswahl());
  seite.querySelectorAll('[data-hoch]').forEach(b => b.onclick = () => verschieben('/api/app/notenschemen', liste, Number(b.dataset.hoch), -1, kontext).catch(err => toast(err.message, true)));
  seite.querySelectorAll('[data-runter]').forEach(b => b.onclick = () => verschieben('/api/app/notenschemen', liste, Number(b.dataset.runter), 1, kontext).catch(err => toast(err.message, true)));
  seite.querySelector('[data-sortieren]').onclick = async () => { await senden('/api/app/notenschemen/reihenfolge', { ids: null }, 'PUT'); toast('Alphabetisch sortiert.'); neuLaden(kontext); };
  seite.querySelector('[data-gesamt-speichern]').onclick = async () => {
    const v = seite.querySelector('[data-gesamt]').value;
    try { await senden('/api/app/notenschemen/gesamt', { id: v ? Number(v) : null }, 'PUT'); toast('Gesamtnotenschema gespeichert.'); }
    catch (err) { toast(err.message, true); }
  };
}

/** Notenschema bearbeiten: Name, aktiv und Noten mit optionalem Wert (Reihenfolge per Pfeil, neue Zeile über „+“). */
function schemaFormular(s, kontext) {
  let noten = (s?.noten ?? []).map(n => ({ text: n.text, wert: n.wert ?? null }));
  const zeilen = () => `${noten.map((n, i) => `<tr>
      <td class="text-nowrap"><button type="button" class="kbe-iknopf" data-n-hoch="${i}" aria-label="Nach oben" ${i === 0 ? 'disabled' : ''}><i class="bi bi-arrow-up"></i></button>
        <button type="button" class="kbe-iknopf" data-n-runter="${i}" aria-label="Nach unten" ${i === noten.length - 1 ? 'disabled' : ''}><i class="bi bi-arrow-down"></i></button></td>
      <td><input class="form-control form-control-sm" data-n-text="${i}" value="${e(n.text)}" maxlength="30" aria-label="Note"></td>
      <td><input type="number" step="any" class="form-control form-control-sm kbe-wertfeld" data-n-wert="${i}" value="${n.wert ?? ''}" placeholder="–" aria-label="Wert"></td>
      <td class="text-end"><button type="button" class="kbe-iknopf rot" data-n-weg="${i}" aria-label="Note löschen"><i class="bi bi-x-lg"></i></button></td></tr>`).join('')}
    <tr class="kbe-neuzeile"><td></td><td colspan="2"><input class="form-control form-control-sm" data-n-neu maxlength="30" placeholder="Neue Note eingeben und Enter drücken" aria-label="Neue Note"></td>
      <td class="text-end"><button type="button" class="kbe-iknopf gruen" data-n-plus aria-label="Note hinzufügen"><i class="bi bi-plus-lg"></i></button></td></tr>`;
  const box = dialog({
    titel: s ? `Notenschema ${s.name}` : 'Neues Notenschema', breit: true,
    inhalt: `<div class="kbe-formular">
      <div class="kbe-reihe"><div class="flex-grow-1"><label class="form-label" for="ns-name">Name</label><input class="form-control" id="ns-name" data-name value="${e(s?.name ?? '')}" maxlength="100" required></div>
        <label class="kbe-check"><input type="checkbox" class="form-check-input" data-aktiv ${s?.aktiv ?? true ? 'checked' : ''}> aktiv</label></div>
      <h3 class="kbe-unter">Noten</h3>
      <table class="kbe-notentabelle"><thead><tr><th><span class="visually-hidden">Reihenfolge</span></th><th>Note</th><th>Wert</th><th></th></tr></thead><tbody data-noten>${zeilen()}</tbody></table>
      <p class="kbe-hilfe">Werte sind optional. Nur wenn alle Noten einen Wert haben, werden Durchschnittsnoten berechnet.</p></div>`,
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async b => {
      uebernehmen(b);
      const neu = b.querySelector('[data-n-neu]').value.trim();
      if (neu) noten.push({ text: neu, wert: null });
      await senden(s ? `/api/app/notenschemen/${s.id}` : '/api/app/notenschemen', { name: b.querySelector('[data-name]').value, aktiv: b.querySelector('[data-aktiv]').checked, noten }, s ? 'PUT' : 'POST');
      toast('Gespeichert.');
      neuLaden(kontext);
    } }],
  });
  function uebernehmen(b) {
    b.querySelectorAll('[data-n-text]').forEach(f => { noten[Number(f.dataset.nText)].text = f.value; });
    b.querySelectorAll('[data-n-wert]').forEach(f => { noten[Number(f.dataset.nWert)].wert = f.value === '' ? null : Number(f.value); });
  }
  const neuZeichnen = (fokusNeu = false) => {
    box.querySelector('[data-noten]').innerHTML = zeilen();
    binden();
    if (fokusNeu) box.querySelector('[data-n-neu]').focus();
  };
  const hinzufuegen = () => {
    uebernehmen(box);
    const f = box.querySelector('[data-n-neu]');
    if (!f.value.trim()) { f.focus(); return; }
    noten.push({ text: f.value.trim(), wert: null });
    neuZeichnen(true);
  };
  function binden() {
    box.querySelectorAll('[data-n-hoch], [data-n-runter]').forEach(b => b.onclick = () => {
      uebernehmen(box);
      const i = Number(b.dataset.nHoch ?? b.dataset.nRunter), j = i + (b.dataset.nHoch !== undefined ? -1 : 1);
      [noten[i], noten[j]] = [noten[j], noten[i]];
      neuZeichnen();
    });
    box.querySelectorAll('[data-n-weg]').forEach(b => b.onclick = () => { uebernehmen(box); noten.splice(Number(b.dataset.nWeg), 1); neuZeichnen(); });
    box.querySelector('[data-n-plus]').onclick = hinzufuegen;
    box.querySelector('[data-n-neu]').onkeydown = ev => { if (ev.key === 'Enter') { ev.preventDefault(); hinzufuegen(); } };
  }
  binden();
}

// ------------------------------------------------------------------ Prüfungsarten

async function pruefungsarten(seite, kontext) {
  const [d, ns] = await Promise.all([holen('/api/app/pruefungsarten?alle=true'), holen('/api/app/notenschemen')]);
  const liste = d.arten;
  const schemen = ns.schemen;
  kontext.titel('Prüfungsarten', `<button class="btn btn-primary" data-neu>${icon('plus')}Neu</button>`, String(liste.length));
  const zeitraum = a => a.maxZeitraum || a.zeitraumTage ? `${a.maxZeitraum} / ${a.zeitraumTage}` : '–';
  const zahl = n => n ? String(n) : '–';
  seite.innerHTML = `<div class="kbe">
    <div class="nl-leiste"><label class="nl-suche"><i class="bi bi-search" aria-hidden="true"></i><input type="search" data-suche placeholder="Suche (Kurz- oder Langname)" aria-label="Prüfungsarten suchen"></label></div>
    <div class="nl-tabelle kbe-tabelle">${liste.length ? `<table><thead><tr>
        <th class="nl-wahl"><input type="checkbox" class="nl-rund" data-alle aria-label="Alle auswählen"></th><th>Prüfungsart</th><th>Langname</th>
        <th class="text-center">Max pro Tag</th><th class="text-center">Max pro Woche</th><th class="text-center" title="Prüfungen / innerhalb von Tagen">Max pro Tage</th>
        <th class="text-center">Keine Prüfung nach</th><th class="text-center">Gruppierung</th><th>Notenschema</th><th class="text-center">aktiv</th><th class="text-end"><span class="visually-hidden">Aktionen</span></th></tr></thead>
      <tbody>${liste.map((a, i) => `<tr data-zeile data-text="${e(`${a.kurz} ${a.name}`.toLowerCase())}">
        <td class="nl-wahl"><input type="checkbox" class="nl-rund" data-wahl="${a.id}" aria-label="${e(a.kurz)} auswählen"></td>
        <td><button type="button" class="kbe-art" style="color:${e(a.vordergrund)};background:${e(a.hintergrund)}" data-bearbeiten="${i}">${e(a.kurz)}</button></td>
        <td>${e(a.name)}${a.muendlich ? ' <span class="nl-chip grau">mündlich</span>' : ''}</td>
        <td class="text-center">${zahl(a.maxTag)}</td><td class="text-center">${zahl(a.maxWoche)}</td><td class="text-center text-nowrap">${zeitraum(a)}</td>
        <td class="text-center">${a.freieTage ? `${a.freieTage} ${a.freieTage === 1 ? 'Tag' : 'Tagen'}` : '–'}</td><td class="text-center">${zahl(a.gruppierung)}</td>
        <td>${a.notenschema ? e(a.notenschema) : '<span class="kbe-leise">individuell</span>'}</td>
        <td class="text-center">${a.aktiv ? '<i class="bi bi-check-lg kbe-ja" aria-label="aktiv"></i>' : '<span class="visually-hidden">inaktiv</span>'}</td>
        <td class="text-end text-nowrap">
          <button type="button" class="kbe-iknopf" data-hoch="${i}" title="Nach oben" aria-label="Nach oben" ${i === 0 ? 'disabled' : ''}><i class="bi bi-arrow-up"></i></button>
          <button type="button" class="kbe-iknopf" data-runter="${i}" title="Nach unten" aria-label="Nach unten" ${i === liste.length - 1 ? 'disabled' : ''}><i class="bi bi-arrow-down"></i></button>
          <button type="button" class="kbe-iknopf" data-bearbeiten="${i}" title="Bearbeiten" aria-label="${e(a.kurz)} bearbeiten"><i class="bi bi-pencil"></i></button>
          <button type="button" class="kbe-iknopf rot" data-loeschen="${a.id}" title="Löschen" aria-label="${e(a.kurz)} löschen"><i class="bi bi-trash3"></i></button></td></tr>`).join('')}</tbody></table>` :
      '<div class="s-leer"><span class="s-leer-symbol"><i class="bi bi-clipboard-check"></i></span><b>Noch keine Prüfungsarten</b><p>Legen Sie mit „Neu“ die erste Prüfungsart an.</p></div>'}
      ${fuss(liste.length, `<button type="button" class="nl-knopf" data-neu2>${icon('plus')}Neu</button>
        <button type="button" class="nl-knopf" data-sortieren ${liste.length > 1 ? '' : 'disabled'} title="Alphabetisch nach Kurzname sortieren"><i class="bi bi-sort-alpha-down"></i> Sortieren</button>
        <button type="button" class="nl-knopf rot" data-auswahl-loeschen disabled><i class="bi bi-trash3"></i> Löschen</button>
        <button type="button" class="nl-knopf" data-bericht>${icon('csv')}Bericht</button>`)}</div></div>`;

  seite.querySelector('[data-suche]').oninput = ev => {
    const f = ev.target.value.trim().toLowerCase();
    seite.querySelectorAll('[data-zeile]').forEach(z => z.classList.toggle('d-none', !!f && !z.dataset.text.includes(f)));
  };
  const auswahl = auswahlBinden(seite, ids => { seite.querySelector('[data-auswahl-loeschen]').disabled = !ids.length; });
  const loeschen = async ids => {
    const namen = liste.filter(a => ids.includes(a.id)).map(a => a.kurz);
    if (!ids.length || !await bestaetigen('Prüfungsarten löschen', `${namen.length === 1 ? `„${namen[0]}“` : `${namen.length} Prüfungsarten`} löschen?`)) return;
    await senden('/api/app/pruefungsarten/loeschen', { ids });
    toast('Gelöscht.');
    neuLaden(kontext);
  };
  document.querySelector('[data-neu]').onclick = () => artFormular(null, schemen, kontext);
  seite.querySelector('[data-neu2]').onclick = () => artFormular(null, schemen, kontext);
  seite.querySelectorAll('[data-bearbeiten]').forEach(b => b.onclick = () => artFormular(liste[Number(b.dataset.bearbeiten)], schemen, kontext));
  seite.querySelectorAll('[data-loeschen]').forEach(b => b.onclick = () => loeschen([Number(b.dataset.loeschen)]));
  seite.querySelector('[data-auswahl-loeschen]').onclick = () => loeschen(auswahl());
  seite.querySelectorAll('[data-hoch]').forEach(b => b.onclick = () => verschieben('/api/app/pruefungsarten', liste, Number(b.dataset.hoch), -1, kontext).catch(err => toast(err.message, true)));
  seite.querySelectorAll('[data-runter]').forEach(b => b.onclick = () => verschieben('/api/app/pruefungsarten', liste, Number(b.dataset.runter), 1, kontext).catch(err => toast(err.message, true)));
  seite.querySelector('[data-sortieren]').onclick = async () => { await senden('/api/app/pruefungsarten/reihenfolge', { ids: null }, 'PUT'); toast('Alphabetisch sortiert.'); neuLaden(kontext); };
  seite.querySelector('[data-bericht]').onclick = () => herunterladen('pruefungsarten.csv', [
    ['Kurzname', 'Langname', 'Art', 'Max pro Tag', 'Max pro Woche', 'Max pro Zeitbereich', 'innerhalb Tagen', 'Keine Prüfung nach freien Tagen', 'Gruppierung', 'Notenschema', 'Gewichtung', 'aktiv'].join(';'),
    ...liste.map(a => [a.kurz, a.name, a.muendlich ? 'mündlich' : 'schriftlich', a.maxTag, a.maxWoche, a.maxZeitraum, a.zeitraumTage, a.freieTage, a.gruppierung, a.notenschema, String(a.gewichtung).replace('.', ','), a.aktiv ? 'ja' : 'nein'].map(csvZelle).join(';')),
  ].join('\r\n'));
}

/** Formular „Neue Prüfungsart“ bzw. Bearbeiten: Speichern, Speichern & Neu, Abbrechen. */
function artFormular(a, schemen, kontext) {
  const leer = { kurz: '', name: '', aktiv: true, muendlich: false, angekuendigt: true, uebergeben: true, vordergrund: '#000000', hintergrund: '#ffffff', maxTag: 0, maxWoche: 0, maxZeitraum: 0, zeitraumTage: 0, gruppierung: 0, freieTage: 0, mitWochenende: true, notenschemaId: null, gewichtung: 1 };
  let x = { ...leer, ...(a ?? {}) };
  let gespeichert = false;
  const zahl = (n, label, extra = '') => `<label class="kbe-max"><span>${e(label)}</span><input type="number" class="form-control form-control-sm" min="0" max="99" data-f="${n}" value="${x[n] ?? 0}">${extra}</label>`;
  const inhalt = () => `<div class="kbe-formular">
    <div class="kbe-reihe"><div class="flex-grow-1"><label class="form-label" for="pa-kurz">Kurzname</label><input class="form-control" id="pa-kurz" data-f="kurz" value="${e(x.kurz)}" maxlength="20" required></div>
      <label class="kbe-check"><input type="checkbox" class="form-check-input" data-f="aktiv" ${x.aktiv ? 'checked' : ''}> aktiv</label></div>
    <div><label class="form-label" for="pa-name">Langname</label><input class="form-control" id="pa-name" data-f="name" value="${e(x.name)}" maxlength="100"></div>
    <div class="kbe-gruppe"><span class="form-label d-block">Prüfungsart</span><div class="kbe-radios waagrecht">
      <label class="kbe-radio"><input type="radio" name="pa-art" value="0" ${x.muendlich ? '' : 'checked'}><span>Schriftlich</span></label>
      <label class="kbe-radio"><input type="radio" name="pa-art" value="1" ${x.muendlich ? 'checked' : ''}><span>Mündlich</span></label></div></div>
    <div class="kbe-gruppe">
      <label class="kbe-check"><input type="checkbox" class="form-check-input" data-f="angekuendigt" ${x.angekuendigt ? 'checked' : ''}> Angekündigte Prüfung</label>
      <label class="kbe-check"><input type="checkbox" class="form-check-input" data-f="uebergeben" ${x.uebergeben ? 'checked' : ''}> Prüfungen dieser Art an UntPlan übergeben</label></div>
    <div class="kbe-reihe kbe-farben">
      <label><span class="form-label d-block">Vordergrundfarbe</span><input type="color" class="form-control form-control-color" data-f="vordergrund" value="${e(x.vordergrund)}"></label>
      <label><span class="form-label d-block">Hintergrundfarbe</span><input type="color" class="form-control form-control-color" data-f="hintergrund" value="${e(x.hintergrund)}"></label>
      <span class="kbe-art gross" data-vorschau style="color:${e(x.vordergrund)};background:${e(x.hintergrund)}">${e(x.kurz || 'Vorschau')}</span></div>
    <h3 class="kbe-unter">Maximale Anzahl der Prüfungen pro Schüler*in</h3>
    <div class="kbe-maxraster">
      ${zahl('maxTag', 'Pro Tag')}${zahl('maxWoche', 'Pro Kalenderwoche')}
      ${zahl('maxZeitraum', 'Pro Zeitbereich', `<span class="kbe-zwischen">innerhalb von</span><input type="number" class="form-control form-control-sm" min="0" max="99" data-f="zeitraumTage" value="${x.zeitraumTage ?? 0}" aria-label="innerhalb von Tagen"><span class="kbe-zwischen">Tagen</span>`)}
      ${zahl('gruppierung', 'Gruppierung')}
      ${zahl('freieTage', 'Keine Prüfung nach', `<span class="kbe-zwischen">freien Tagen</span><label class="kbe-check ms-2"><input type="checkbox" class="form-check-input" data-f="mitWochenende" ${x.mitWochenende ? 'checked' : ''}> mit Wochenende</label>`)}
    </div>
    <p class="kbe-hilfe">0 = keine Begrenzung.</p>
    <h3 class="kbe-unter">Noten</h3>
    <div class="kbe-reihe">
      <div class="flex-grow-1"><label class="form-label" for="pa-schema">Notenschema</label><select class="form-select" id="pa-schema" data-f="notenschemaId">
        <option value="">– individuell durch die Lehrkraft –</option>${schemen.filter(s => s.aktiv || s.id === x.notenschemaId).map(s => `<option value="${s.id}" ${s.id === x.notenschemaId ? 'selected' : ''}>${e(s.name)}</option>`).join('')}</select></div>
      <div><label class="form-label" for="pa-gewicht">Gewichtungsfaktor</label><input type="number" step="0.1" min="0" max="100" class="form-control kbe-wertfeld" id="pa-gewicht" data-f="gewichtung" value="${x.gewichtung ?? 1}"></div></div>
    <p class="kbe-hilfe" data-schema-hinweis ${x.notenschemaId ? 'hidden' : ''}>Ohne Notenschema wird die Lehrkraft beim Anlegen einer Prüfung dieser Art nach einem individuellen Notenschema gefragt.</p></div>`;
  const lesen = b => {
    const f = n => b.querySelector(`[data-f="${n}"]`);
    const zahlen = ['maxTag', 'maxWoche', 'maxZeitraum', 'zeitraumTage', 'gruppierung', 'freieTage'];
    return {
      kurz: f('kurz').value, name: f('name').value, aktiv: f('aktiv').checked, muendlich: b.querySelector('[name=pa-art]:checked')?.value === '1',
      angekuendigt: f('angekuendigt').checked, uebergeben: f('uebergeben').checked, vordergrund: f('vordergrund').value, hintergrund: f('hintergrund').value,
      ...Object.fromEntries(zahlen.map(n => [n, Number(f(n).value || 0)])), mitWochenende: f('mitWochenende').checked,
      notenschemaId: f('notenschemaId').value ? Number(f('notenschemaId').value) : null, gewichtung: Number(f('gewichtung').value || 0),
    };
  };
  const speichern = async b => {
    const daten = lesen(b);
    const r = await senden(a ? `/api/app/pruefungsarten/${a.id}` : '/api/app/pruefungsarten', daten, a ? 'PUT' : 'POST');
    gespeichert = true;
    toast(`Prüfungsart ${r.kurz} gespeichert.`);
    return r;
  };
  const binden = b => {
    const vorschau = () => { const v = b.querySelector('[data-vorschau]'); v.style.color = b.querySelector('[data-f="vordergrund"]').value; v.style.background = b.querySelector('[data-f="hintergrund"]').value; v.textContent = b.querySelector('[data-f="kurz"]').value || 'Vorschau'; };
    b.querySelectorAll('[data-f="vordergrund"], [data-f="hintergrund"], [data-f="kurz"]').forEach(f => f.addEventListener('input', vorschau));
    b.querySelector('[data-f="notenschemaId"]').addEventListener('change', ev => { b.querySelector('[data-schema-hinweis]').hidden = !!ev.target.value; });
  };
  const box = dialog({
    titel: a ? `Prüfungsart ${a.kurz}` : 'Neue Prüfungsart', breit: true, inhalt: inhalt(),
    knoepfe: [
      { text: 'Abbrechen' },
      // „Speichern & Neu“ nur beim Anlegen: Formular bleibt offen und wird geleert
      ...(a ? [] : [{ text: 'Speichern & Neu', aktion: async b => { await speichern(b); x = { ...leer }; b.querySelector('.modal-body').innerHTML = inhalt(); binden(b); b.querySelector('[data-f="kurz"]').focus(); return false; } }]),
      { text: 'Speichern', art: 'haupt', aktion: async b => { await speichern(b); } },
    ],
  });
  binden(box);
  box.closest('.modal').addEventListener('hidden.bs.modal', () => { if (gespeichert) neuLaden(kontext); });
}
