// Administration | Stundenplaneinstellungen (#/planeinstellungen) im neuen Design: Reiter „Allgemein“ (welches Stundenplanformat für die
// Klassen-, Lehrkraft- und Raumpläne gilt) und „Stundenplanformate“ (Liste, anlegen, bearbeiten, löschen). Ein Format legt fest, was auf den
// Stundenkärtchen steht (1.–5. Element mit Kurz-/Langname), welche Tage und Uhrzeiten der Plan zeigt, die Stundenbeschriftung, welche
// Aktivitäten erscheinen und was hervorgehoben wird. Ohne Format gilt die Standarddarstellung.
import { e, holen, senden, api, toast, lade, bestaetigen } from '../ui.js';
import { cssLaden } from './kbeinstellungen.js';

const BASIS = '/api/app/admin/planformate';
const REITER = [['allgemein', 'Allgemein', 'sliders2'], ['formate', 'Stundenplanformate', 'grid-3x3-gap']];
const ARTEN = { klasse: 'Klasse', lehrer: 'Lehrkraft', fach: 'Fach', raum: 'Raum', notiz: 'Notizen für Schüler*innen' };
const NAMEN = [['kurz', 'Kurzname'], ['lang', 'Langname'], ['langkurz', 'Langname (Kurzname)']];
const TAGE = ['Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa', 'So'];

/** Vorlage eines neuen Formats (wie die Standarddarstellung des Klassenplans). */
const neuesFormat = () => ({
  id: 0, name: '', details: true, unangekuendigtePruefungen: false, farben: true, zeiten: true, stundennummer: true,
  elemente: [{ art: 'fach', name: 'kurz' }, { art: 'lehrer', name: 'kurz' }, { art: 'raum', name: 'kurz' }],
  tage: [0, 1, 2, 3, 4], zeitVon: '', zeitBis: '',
  entfall: true, bereitschaften: true, sprechstunden: true, buchungen: true,
  aenderungen: true, pruefungen: true, entfaelle: true, symbole: true,
});

export async function zeigen(seite, kontext, [reiterParam]) {
  cssLaden();
  if (!kontext.ich?.admin) { seite.innerHTML = '<p class="nl-hinweis"><i class="bi bi-lock"></i>Nur für die Administration.</p>'; return; }
  let reiter = REITER.some(r => r[0] === reiterParam) ? reiterParam : 'allgemein';
  let d = await holen(BASIS);
  let zuordnung = structuredClone(d.zuordnung);
  let bearbeiten = null;

  const karte = (titel, symbol, inhalt, rechts = '') => `<section class="kbe-karte"><header><span class="kbe-symbol"><i class="bi bi-${symbol}" aria-hidden="true"></i></span><h2>${e(titel)}</h2>${rechts}</header><div class="kbe-rumpf">${inhalt}</div></section>`;
  const hilfe = t => t ? `<p class="kbe-hilfe">${t}</p>` : '';
  const zeile = (label, inhalt, text = '') => `<div class="kbe-zeile"><div class="kbe-label"><span>${e(label)}</span></div><div class="kbe-wert">${inhalt}${hilfe(text)}</div></div>`;
  const formatWahl = (feld, wert) => `<select class="form-select kbe-auswahl" data-zuordnung="${feld}" aria-label="Stundenplanformat">
      <option value="">Standard</option>${d.formate.map(f => `<option value="${f.id}" ${wert === f.id ? 'selected' : ''}>${e(f.name)}</option>`).join('')}</select>`;

  // ---------------------------------------------------------------- Reiter „Allgemein“

  const allgemein = () => `
    ${karte('Stundenpläne', 'calendar-week', `
      ${zeile('Format für Klassen-Stundenpläne festlegen', formatWahl('klasse', zuordnung.klasse), 'Gilt für die Pläne der Klassen – auch für Schüler*innen und Erziehungsberechtigte („Mein Stundenplan“).')}
      ${zeile('Format für Lehrkraft-Stundenpläne festlegen', formatWahl('lehrer', zuordnung.lehrer))}
      ${zeile('Format für Raum-Stundenpläne festlegen', formatWahl('raum', zuordnung.raum))}
      <div class="kbe-zeile kbe-schalterzeile"><label class="kbe-schalter" for="pf-kollegium">
        <input type="checkbox" role="switch" id="pf-kollegium" data-kollegium ${zuordnung.kollegium ? 'checked' : ''}><span class="kbe-schiene" aria-hidden="true"></span>
        <span class="kbe-schalter-text">Formate auch für das Kollegium verwenden</span></label>${hilfe('Aus: Lehrkräfte und Verwaltung sehen alle Pläne in der Standarddarstellung; die Formate gelten nur für Schüler*innen und Erziehungsberechtigte.')}</div>`)}
    <div class="kbe-leiste"><span class="kbe-stand" data-stand>${JSON.stringify(zuordnung) !== JSON.stringify(d.zuordnung) ? 'Nicht gespeicherte Änderungen' : 'Alle Änderungen gespeichert'}</span>
      <button type="button" class="nl-knopf nw-primaer" data-zuordnung-speichern ${JSON.stringify(zuordnung) !== JSON.stringify(d.zuordnung) ? '' : 'disabled'}>Speichern</button></div>`;

  // ---------------------------------------------------------------- Reiter „Stundenplanformate“

  const verwendet = f => [['klasse', 'Klassen'], ['lehrer', 'Lehrkräfte'], ['raum', 'Räume']].filter(([k]) => d.zuordnung[k] === f.id).map(([, t]) => t);
  const formate = () => karte('Stundenplanformate', 'grid-3x3-gap', d.formate.length
    ? `<div class="nl-tabelle pf-liste"><table><thead><tr><th>Name</th><th>Stundenkärtchen</th><th>Verwendet für</th><th></th></tr></thead><tbody>
        ${d.formate.map(f => `<tr><td class="nl-name">${e(f.name)}</td><td class="nl-unter">${e(f.elemente.map(x => ARTEN[x.art] ?? x.art).join(' · '))}</td>
          <td>${verwendet(f).map(t => `<span class="nl-chip gruen">${e(t)}</span>`).join(' ') || '<span class="nl-unter">–</span>'}</td>
          <td class="text-end text-nowrap"><button type="button" class="nl-icon sichtbar" data-bearbeiten="${f.id}" title="Bearbeiten" aria-label="${e(f.name)} bearbeiten"><i class="bi bi-pencil"></i></button>
            <button type="button" class="nl-icon sichtbar rot" data-loeschen="${f.id}" title="Löschen" aria-label="${e(f.name)} löschen"><i class="bi bi-trash"></i></button></td></tr>`).join('')}
      </tbody></table></div>`
    : '<p class="nl-unter m-0">Noch keine Stundenplanformate – alle Pläne erscheinen in der Standarddarstellung.</p>',
    '<button type="button" class="nl-hinzu ms-auto" data-neu><i class="bi bi-plus-lg"></i>Neues Stundenplanformat</button>');

  // ---------------------------------------------------------------- Editor

  const editor = f => {
    const schalter = (n, label, text = '') => `<div class="kbe-zeile kbe-schalterzeile"><label class="kbe-schalter" for="pf-${n}">
        <input type="checkbox" role="switch" id="pf-${n}" data-f="${n}" ${f[n] ? 'checked' : ''}><span class="kbe-schiene" aria-hidden="true"></span>
        <span class="kbe-schalter-text">${e(label)}</span></label>${hilfe(text)}</div>`;
    const elementZeile = (x, i) => `<div class="pf-element" data-element="${i}">
        <span class="pf-nr">${i + 1}.</span>
        <select class="form-select" data-e-art aria-label="${i + 1}. Element">${Object.entries(ARTEN).map(([k, t]) => `<option value="${k}" ${x.art === k ? 'selected' : ''}>${e(t)}</option>`).join('')}</select>
        <select class="form-select" data-e-name aria-label="Namensform" ${x.art === 'notiz' ? 'disabled' : ''}>${NAMEN.map(([k, t]) => `<option value="${k}" ${x.name === k ? 'selected' : ''}>${e(t)}</option>`).join('')}</select>
        <button type="button" class="nl-icon sichtbar" data-e-hoch="${i}" ${i === 0 ? 'disabled' : ''} title="Nach oben" aria-label="Nach oben"><i class="bi bi-arrow-up"></i></button>
        <button type="button" class="nl-icon sichtbar rot" data-e-weg="${i}" ${f.elemente.length <= 1 ? 'disabled' : ''} title="Entfernen" aria-label="Entfernen"><i class="bi bi-x-lg"></i></button></div>`;
    return `<div class="pf-editor">
      ${karte(f.id ? 'Stundenplanformat bearbeiten' : 'Neues Stundenplanformat', 'pencil-square', `
        ${zeile('Name', `<input class="form-control kbe-auswahl" data-f="name" maxlength="60" value="${e(f.name)}" placeholder="z. B. Schüler*innen">`)}`)}
      ${karte('Allgemein', 'sliders2', `
        ${schalter('details', 'Zugriff auf Stundendetails', 'Antippen einer Stunde öffnet die Details (Lehrstoff, Hausaufgaben, Notizen).')}
        ${schalter('farben', 'Individuelle Farben anzeigen', 'Fachfarben auf den Stundenkärtchen.')}`)}
      ${karte('Einstellungen des Stundenplan-Rasters', 'layout-three-columns', `
        ${zeile('Stundenbeschriftung', `<div class="pf-checks">
          <label><input type="checkbox" data-f="zeiten" ${f.zeiten ? 'checked' : ''}> Beginn- und Endzeit anzeigen</label>
          <label><input type="checkbox" data-f="stundennummer" ${f.stundennummer ? 'checked' : ''}> Stundennummer anzeigen</label></div>`)}`)}
      ${karte('Anordnung auf dem Stundenkärtchen', 'card-list', `
        <p class="kbe-hilfe mt-0">Das 1. Element steht fett oben auf dem Kärtchen, die weiteren darunter.</p>
        <div class="pf-elemente">${f.elemente.map(elementZeile).join('')}</div>
        ${f.elemente.length < 5 ? '<button type="button" class="nl-knopf mt-2" data-e-neu><i class="bi bi-plus-lg"></i>Element</button>' : ''}`)}
      ${karte('Stundenplananzeige', 'calendar3', `
        ${zeile('Tage', `<div class="pf-tage" role="group" aria-label="Tage">${TAGE.map((t, i) => `<button type="button" class="${f.tage.includes(i) ? 'an' : ''}" data-tag="${i}" aria-pressed="${f.tage.includes(i)}">${t}</button>`).join('')}</div>`,
          'Tage, die der Stundenplan zeigt.')}
        ${zeile('Uhrzeiten', `<span class="kbe-bereich"><input type="time" class="form-control kbe-zeit" data-f="zeitVon" value="${e(f.zeitVon)}" aria-label="Beginnzeit"><span>bis</span>
          <input type="time" class="form-control kbe-zeit" data-f="zeitBis" value="${e(f.zeitBis)}" aria-label="Endzeit"></span>`, 'Leer = alle Stunden des Zeitrasters. Bitte auch Pausenaufsichten an Randzeiten berücksichtigen.')}`)}
      ${karte('Aktivitäten anzeigen', 'eye', `
        ${schalter('entfall', 'Entfallenen Unterricht anzeigen')}
        ${schalter('bereitschaften', 'Bereitschaften anzeigen')}
        ${schalter('sprechstunden', 'Sprechstunden anzeigen')}
        ${schalter('buchungen', 'Raumbuchungen anzeigen')}`)}
      ${karte('Voreinstellung der Hervorhebungen', 'highlighter', `
        <p class="kbe-hilfe mt-0">Hervorgehobene Stunden sind farbig markiert (geändert grün, Prüfung gelb, Entfall rot) und tragen Symbole.</p>
        ${schalter('aenderungen', 'Änderungen hervorheben')}
        ${schalter('pruefungen', 'Prüfungen hervorheben')}
        ${schalter('entfaelle', 'Entfälle hervorheben')}
        ${schalter('symbole', 'Symbole auf Stundenkärtchen anzeigen')}`)}
      <div class="kbe-leiste"><span class="kbe-stand">${f.id ? 'Format bearbeiten' : 'Neues Format'}</span>
        <button type="button" class="nl-knopf" data-abbrechen>Abbrechen</button>
        <button type="button" class="nl-knopf nw-primaer" data-format-speichern>Speichern</button></div>
    </div>`;
  };

  // ---------------------------------------------------------------- Zeichnen und Binden

  const zeichnen = () => {
    kontext.titel('Stundenplaneinstellungen');
    seite.innerHTML = `<div class="kbe pf">
      ${bearbeiten ? '' : `<nav class="kbe-reiter" role="tablist" aria-label="Kategorien">${REITER.map(([r, t, s]) => `<button type="button" role="tab" aria-selected="${r === reiter}" class="${r === reiter ? 'an' : ''}" data-reiter="${r}"><i class="bi bi-${s}" aria-hidden="true"></i>${t}</button>`).join('')}</nav>`}
      <div class="kbe-inhalt" role="tabpanel">${bearbeiten ? editor(bearbeiten) : reiter === 'formate' ? formate() : allgemein()}</div></div>`;
    binden();
  };

  const binden = () => {
    seite.querySelectorAll('[data-reiter]').forEach(b => b.onclick = () => { reiter = b.dataset.reiter; history.replaceState(null, '', `#/planeinstellungen/${reiter}`); zeichnen(); });
    // Allgemein: Zuordnung
    seite.querySelectorAll('[data-zuordnung]').forEach(s => s.onchange = () => { zuordnung[s.dataset.zuordnung] = s.value ? Number(s.value) : null; zeichnen(); });
    const k = seite.querySelector('[data-kollegium]');
    if (k) k.onchange = () => { zuordnung.kollegium = k.checked; zeichnen(); };
    seite.querySelector('[data-zuordnung-speichern]')?.addEventListener('click', async ev => {
      ev.currentTarget.disabled = true;
      try {
        d.zuordnung = await senden('/api/app/admin/planformate-zuordnung', zuordnung, 'PUT');
        zuordnung = structuredClone(d.zuordnung);
        toast('Einstellungen gespeichert.');
        zeichnen();
      } catch (err) { toast(err.message, true); ev.currentTarget.disabled = false; }
    });
    // Liste
    seite.querySelector('[data-neu]')?.addEventListener('click', () => { bearbeiten = neuesFormat(); zeichnen(); });
    seite.querySelectorAll('[data-bearbeiten]').forEach(b => b.onclick = () => { bearbeiten = structuredClone(d.formate.find(f => f.id === Number(b.dataset.bearbeiten))); zeichnen(); });
    seite.querySelectorAll('[data-loeschen]').forEach(b => b.onclick = async () => {
      const f = d.formate.find(x => x.id === Number(b.dataset.loeschen));
      if (!await bestaetigen('Format löschen', `Stundenplanformat „${f.name}“ löschen? Pläne, die es verwenden, erscheinen wieder in der Standarddarstellung.`)) return;
      try { await api(`${BASIS}/${f.id}`, { methode: 'DELETE' }); d = await holen(BASIS); zuordnung = structuredClone(d.zuordnung); toast('Format gelöscht.'); zeichnen(); } catch (err) { toast(err.message, true); }
    });
    if (!bearbeiten) return;
    // Editor
    const f = bearbeiten;
    seite.querySelectorAll('[data-f]').forEach(x => {
      const lesen = () => { f[x.dataset.f] = x.type === 'checkbox' ? x.checked : x.value; };
      x.addEventListener('change', lesen);
      if (x.type === 'text' || x.tagName === 'INPUT' && x.type !== 'checkbox') x.addEventListener('input', lesen);
    });
    seite.querySelectorAll('[data-tag]').forEach(b => b.onclick = () => {
      const t = Number(b.dataset.tag);
      f.tage = f.tage.includes(t) ? f.tage.filter(x => x !== t) : [...f.tage, t].sort();
      b.classList.toggle('an', f.tage.includes(t));
      b.setAttribute('aria-pressed', String(f.tage.includes(t)));
    });
    seite.querySelectorAll('[data-element]').forEach(z => {
      const i = Number(z.dataset.element);
      z.querySelector('[data-e-art]').onchange = ev => { f.elemente[i].art = ev.target.value; zeichnen(); };
      z.querySelector('[data-e-name]').onchange = ev => { f.elemente[i].name = ev.target.value; };
    });
    seite.querySelectorAll('[data-e-hoch]').forEach(b => b.onclick = () => { const i = Number(b.dataset.eHoch); [f.elemente[i - 1], f.elemente[i]] = [f.elemente[i], f.elemente[i - 1]]; zeichnen(); });
    seite.querySelectorAll('[data-e-weg]').forEach(b => b.onclick = () => { f.elemente.splice(Number(b.dataset.eWeg), 1); zeichnen(); });
    seite.querySelector('[data-e-neu]')?.addEventListener('click', () => {
      const frei = Object.keys(ARTEN).find(a => !f.elemente.some(x => x.art === a)) ?? 'notiz';
      f.elemente.push({ art: frei, name: 'kurz' });
      zeichnen();
    });
    seite.querySelector('[data-abbrechen]').onclick = () => { bearbeiten = null; zeichnen(); };
    seite.querySelector('[data-format-speichern]').onclick = async ev => {
      ev.currentTarget.disabled = true;
      try {
        await senden(f.id ? `${BASIS}/${f.id}` : BASIS, f, f.id ? 'PUT' : 'POST');
        d = await holen(BASIS);
        zuordnung = structuredClone(d.zuordnung);
        bearbeiten = null;
        reiter = 'formate';
        toast('Stundenplanformat gespeichert.');
        zeichnen();
      } catch (err) { toast(err.message, true); ev.currentTarget.disabled = false; }
    };
  };

  seite.innerHTML = lade();
  zeichnen();
}
