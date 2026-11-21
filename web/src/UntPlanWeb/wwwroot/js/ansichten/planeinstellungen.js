// Administration | Stundenplaneinstellungen (#/planeinstellungen) – Aufbau wie die Vorlage: Unterstrich-Reiter „Allgemein | Stundenplanformate |
// Sichtbarkeit einschränken | iCal“. „Allgemein“: Allgemeine Einstellungen (Links zu Videokonferenzen) und je ein Kasten für Klassen-, Lehrkraft-,
// Schüler- und Raum-Stundenplan mit dem Format („Stundenplanformat“ als schwebendes Label), unten rechts „Speichern“. „Stundenplanformate“:
// Liste (Name ↑, Zähler, „Neu“); Zeile anklicken öffnet das Panel „Stundenplanformat bearbeiten“ (zweispaltig: Beschriftung links, runde orange
// Häkchen rechts; Anordnung auf dem Stundenkärtchen als ziehbare Liste mit türkisem Rand). Ohne Format gilt die Standarddarstellung.
import { e, holen, senden, api, toast, lade, bestaetigen } from '../ui.js';
import { cssLaden } from './kbeinstellungen.js';

const BASIS = '/api/app/admin/planformate';
const REITER = [['allgemein', 'Allgemein'], ['formate', 'Stundenplanformate'], ['sichtbarkeit', 'Sichtbarkeit einschränken'], ['ical', 'iCal']];
const ARTEN = { klasse: 'Klasse', lehrer: 'Lehrkraft', fach: 'Fach', raum: 'Raum', notiz: 'Notizen für Schüler*innen' };
const NAMEN = [['kurz', 'Kurzname'], ['lang', 'Langname'], ['langkurz', 'Langname (Kurzname)']];
const TAGE = ['M', 'D', 'M', 'D', 'F', 'S', 'S'];
const TAGNAMEN = ['Montag', 'Dienstag', 'Mittwoch', 'Donnerstag', 'Freitag', 'Samstag', 'Sonntag'];
const PLAENE = [['klasse', 'Klassen-Stundenplan', 'Klassen'], ['lehrer', 'Lehrkraft-Stundenplan', 'Lehrkraft'], ['schueler', 'Schüler-Stundenplan', 'Schüler'], ['raum', 'Raum-Stundenplan', 'Raum']];
const ALLE_TAGE = [0, 1, 2, 3, 4, 5, 6];

/** Vorlage eines neuen Formats (wie die Standarddarstellung des Klassenplans). */
const neuesFormat = () => ({
  id: 0, name: '', details: true, unangekuendigtePruefungen: false, farben: true,
  zeitraster: 'raster', zeiten: true, stundennummer: true, stundenname: false,
  elemente: [{ art: 'fach', name: 'kurz' }, { art: 'lehrer', name: 'kurz' }, { art: 'raum', name: 'kurz' }],
  tage: [...ALLE_TAGE], zeitVon: '', zeitBis: '', kartenZeiten: false, benutzer: false,
  buchungen: true, entfall: true, bereitschaften: true, sprechstunden: true, externeKalender: true,
  aenderungen: true, pruefungen: true, entfaelle: true, externeHervorheben: true, symbole: true,
});

export async function zeigen(seite, kontext, [reiterParam]) {
  cssLaden();
  if (!kontext.ich?.admin) { seite.innerHTML = '<p class="nl-hinweis"><i class="bi bi-lock"></i>Nur für die Administration.</p>'; return; }
  let reiter = REITER.some(r => r[0] === reiterParam) ? reiterParam : 'allgemein';
  let d = await holen(BASIS);
  let zuordnung = structuredClone(d.zuordnung);
  let bearbeiten = null;
  let absteigend = false;
  let ziehen = null;

  // runde orange Häkchen bzw. Radiopunkte wie in der Vorlage
  const haken = (attr, text, an, typ = 'checkbox', name = '') => `<label class="pe-haken"><input type="${typ}" ${name ? `name="${name}"` : ''} ${attr} ${an ? 'checked' : ''}><span class="pe-kreis" aria-hidden="true"></span><span>${text}</span></label>`;
  const feld = (label, inhalt, extra = '') => `<label class="pe-feld ${extra}"><span class="pe-feld-label">${e(label)}</span>${inhalt}</label>`;
  const geaendert = () => JSON.stringify(zuordnung) !== JSON.stringify(d.zuordnung);

  // ---------------------------------------------------------------- Reiter „Allgemein“

  const allgemein = () => `
    <div class="pe-zeile pe-allg">
      <h3 class="pe-titel">Allgemeine Einstellungen</h3>
      <div class="pe-wert">${haken('data-videolinks', 'Links zu Videokonferenzen in Stundendetails', zuordnung.videoLinks)}</div>
    </div>
    ${PLAENE.map(([k, titel, wort]) => `<section class="pe-kasten">
      <h3 class="pe-titel">${titel}</h3>
      <div class="pe-zeile"><div class="pe-text">Format für ${wort}-Stundenpläne festlegen${k === 'schueler' ? '<small>Gilt für Schüler*innen und Erziehungsberechtigte im eigenen Plan und im Klassenplan.</small>' : ''}</div>
        <div class="pe-wert">${feld('Stundenplanformat', `<select class="form-select" data-zuordnung="${k}">
          <option value="">Standard</option>${d.formate.map(f => `<option value="${f.id}" ${zuordnung[k] === f.id ? 'selected' : ''}>${e(f.name)}</option>`).join('')}</select>`)}</div></div>
    </section>`).join('')}
    <div class="pe-fuss"><button type="button" class="pe-knopf primaer" data-zuordnung-speichern ${geaendert() ? '' : 'disabled'}>Speichern</button></div>`;

  // ---------------------------------------------------------------- Reiter „Stundenplanformate“

  const formate = () => {
    const liste = [...d.formate].sort((a, b) => a.name.localeCompare(b.name, 'de', { sensitivity: 'base' }) * (absteigend ? -1 : 1));
    return `<div class="pe-listenkopf"><h3 class="pe-titel">Stundenplanformate <span class="pe-zahl">${d.formate.length}</span></h3>
        <button type="button" class="pe-knopf klein" data-neu>Neu</button></div>
      <table class="pe-tabelle"><thead><tr><th><button type="button" data-sortieren aria-label="Nach Name sortieren">Name <i class="bi bi-arrow-${absteigend ? 'down' : 'up'}" aria-hidden="true"></i></button></th></tr></thead>
        <tbody>${liste.map(f => `<tr data-bearbeiten="${f.id}" tabindex="0" role="button" aria-label="${e(f.name)} bearbeiten"><td>${e(f.name)}</td></tr>`).join('')
          || '<tr class="leer"><td>Keine Stundenplanformate vorhanden. Alle Pläne erscheinen in der Standarddarstellung.</td></tr>'}</tbody></table>`;
  };

  // ---------------------------------------------------------------- Sichtbarkeit / iCal

  const sichtbarkeit = () => `<section class="pe-kasten">
      <h3 class="pe-titel">Sichtbarkeit einschränken</h3>
      <div class="pe-zeile"><div class="pe-text">Wie viele Wochen zurück bzw. im Voraus der Stundenplan sichtbar ist, wird je Rolle festgelegt (Planfenster).</div>
        <div class="pe-wert"><a class="pe-knopf" href="#/admin/rechte">Rechte und Rollen öffnen</a></div></div>
    </section>`;
  const ical = () => `<section class="pe-kasten">
      <h3 class="pe-titel">iCal</h3>
      <div class="pe-zeile"><div class="pe-text">Jede Person kann ihren Stundenplan unter „Profil“ als iCal-Abo (nächste vier Wochen, Entfälle als abgesagt) in einen Kalender übernehmen.
        Es gilt das Planfenster der Rolle.</div>
        <div class="pe-wert"><a class="pe-knopf" href="#/profil">Profil öffnen</a></div></div>
    </section>`;

  // ---------------------------------------------------------------- Panel „Neues Stundenplanformat“

  const editor = f => {
    const alleTage = ALLE_TAGE.every(t => f.tage.includes(t));
    const elementZeile = (x, i) => `<div class="pe-element ${x.art === 'notiz' ? 'notiz' : ''}" data-element="${i}" draggable="true">
        <span class="pe-griff" title="Ziehen zum Sortieren" aria-hidden="true"><i class="bi bi-grip-vertical"></i></span>
        <select class="pe-pille" data-nativ data-e-art aria-label="${i + 1}. Element">${Object.entries(ARTEN).map(([k, t]) => `<option value="${k}" ${x.art === k ? 'selected' : ''}>${e(t)}</option>`).join('')}</select>
        ${x.art === 'notiz' ? '' : `<select class="pe-name" data-nativ data-e-name aria-label="Namensform">${NAMEN.map(([k, t]) => `<option value="${k}" ${x.name === k ? 'selected' : ''}>${e(t)}</option>`).join('')}</select>`}
        <button type="button" class="pe-weg" data-e-weg="${i}" ${f.elemente.length <= 1 ? 'disabled' : ''} title="Entfernen" aria-label="${i + 1}. Element entfernen"><i class="bi bi-x-lg"></i></button></div>`;
    const abschnitt = (titel, links, rechts) => `<div class="pe-abschnitt"><div class="pe-links">${titel ? `<h3 class="pe-titel">${titel}</h3>` : ''}${links}</div><div class="pe-rechts">${rechts}</div></div>`;
    const unter = t => `<p class="pe-unter">${t}</p>`;
    const h = (n, text) => haken(`data-f="${n}"`, text, f[n]);
    return `<div class="pe-panel-hinter" data-schliessen></div>
    <section class="pe-panel" role="dialog" aria-modal="true" aria-labelledby="pe-panel-titel">
      <header><h2 id="pe-panel-titel">${f.id ? 'Stundenplanformat bearbeiten' : 'Neues Stundenplanformat'}</h2>
        <button type="button" class="pe-x" data-abbrechen title="Schließen" aria-label="Schließen"><i class="bi bi-x-lg"></i></button></header>
      <div class="pe-panel-rumpf">
        ${abschnitt('Allgemein', '', feld('Name', `<input class="form-control" data-f="name" maxlength="60" value="${e(f.name)}" autocomplete="off">`, 'breit'))}
        <div class="pe-paare">
          <span>Stundendetails</span>${h('details', 'Zugriff auf Stundendetails')}
          <span>Unangekündigte Prüfungen</span>${h('unangekuendigtePruefungen', 'Unangekündigte Prüfungen anzeigen')}
          <span>Individuelle Farben</span>${h('farben', 'Individuelle Farben anzeigen (bezieht sich nur auf Fächer)')}
        </div>
        ${abschnitt('Einstellungen des Stundenplan-Rasters', unter('Zeitraster-Einstellung'), `<div class="pe-stapel">
          ${haken('data-raster value="raster"', 'Zeitraster', f.zeitraster !== 'uhrzeit', 'radio', 'pe-raster')}
          ${haken('data-raster value="uhrzeit"', 'Tatsächliche Uhrzeit', f.zeitraster === 'uhrzeit', 'radio', 'pe-raster')}</div>`)}
        ${abschnitt('', unter('Stundenbeschriftung'), `<div class="pe-stapel">${h('zeiten', 'Beginn- und Endzeit anzeigen')}${h('stundennummer', 'Stundennummer anzeigen')}${h('stundenname', 'Stundenname anzeigen')}</div>`)}
        ${abschnitt('Anordnung auf dem Stundenkärtchen', [1, 2, 3, 4].map(n => `<p class="pe-unter pe-elementlabel">${n}. Element auf dem Kärtchen<br>(${n === 1 ? 'erforderlich' : 'optional'})</p>`).join(''),
          `<div class="pe-elemente">${f.elemente.map(elementZeile).join('')}
            ${f.elemente.length < 5 ? '<button type="button" class="pe-plus" data-e-neu aria-label="Element hinzufügen">+</button>' : ''}</div>`)}
        ${abschnitt('Stundenplananzeige', unter('Tage auswählen, für die der Stundenplan angezeigt werden soll'), `<div class="pe-tagzeile">
          <div class="pe-tage" role="group" aria-label="Tage">${TAGE.map((t, i) => `<button type="button" class="${!alleTage && f.tage.includes(i) ? 'an' : ''}" data-tag="${i}" aria-pressed="${f.tage.includes(i)}" title="${TAGNAMEN[i]}" ${alleTage ? 'disabled' : ''}>${t}</button>`).join('')}</div>
          <label class="kbe-schalter pe-schalter"><input type="checkbox" role="switch" data-alletage ${alleTage ? 'checked' : ''}><span class="kbe-schiene" aria-hidden="true"></span><span>Alle Tage des Zeitrasters</span></label></div>`)}
        ${abschnitt('', unter('Uhrzeiten auswählen, für die der Stundenplan angezeigt werden soll'), `<div class="pe-zeiten">
          ${feld('Beginnzeit', `<input type="time" class="form-control" data-f="zeitVon" value="${e(f.zeitVon)}">`)}
          ${feld('Endzeit', `<input type="time" class="form-control" data-f="zeitBis" value="${e(f.zeitBis)}">`)}</div>`)}
        ${abschnitt('Zusätzliche Information auf dem Stundenkärtchen', '', `<div class="pe-stapel">${h('kartenZeiten', 'Beginn- und Endzeit anzeigen')}${h('benutzer', 'Benutzer anzeigen (Info-Icon auf Stundenkärtchen)')}</div>`)}
        ${abschnitt('Aktivitäten anzeigen', unter('Wählen Sie zusätzliche Aktivitäten, die im Stundenplan angezeigt werden sollen')
          + unter('Einige Plattform-Applikationen, z.B. externe Kalender, können Veranstaltungen im neuen Stundenplan anzeigen'), `<div class="pe-stapel">
          ${h('buchungen', 'Nicht bestätigte Buchungen anzeigen')}${h('entfall', 'Entfallenen Unterricht anzeigen')}${h('bereitschaften', 'Bereitschaften anzeigen')}
          ${h('sprechstunden', 'Sprechstunden anzeigen')}${h('externeKalender', 'Zeige externe Kalender')}</div>`)}
        ${abschnitt('Voreinstellung der Hervorhebungen', unter('Sie können im Stundenplan bestimmte Stunden hervorheben wie geänderte Stunden, Prüfungen oder Entfälle, sowie externe Kalender. Wählen Sie die Stunden, die standardmäßig hervorgehoben sein sollen'), `<div class="pe-stapel">
          ${h('aenderungen', 'Änderungen hervorheben')}${h('pruefungen', 'Prüfungen hervorheben')}${h('entfaelle', 'Entfälle hervorheben')}
          ${h('externeHervorheben', 'Externe Kalender hervorheben')}${h('symbole', 'Symbole auf Stundenkärtchen anzeigen')}</div>`)}
      </div>
      <footer>${f.id ? '<button type="button" class="pe-knopf rot" data-format-loeschen>Löschen</button>' : ''}
        <button type="button" class="pe-knopf" data-abbrechen>Abbrechen</button>
        <button type="button" class="pe-knopf primaer" data-format-speichern>Speichern</button></footer>
    </section>`;
  };

  // ---------------------------------------------------------------- Zeichnen und Binden

  const zeichnen = () => {
    kontext.titel('Stundenplaneinstellungen');
    const scroll = seite.querySelector('.pe-panel-rumpf')?.scrollTop ?? 0;
    seite.innerHTML = `<div class="pe">
      <nav class="pe-reiter" role="tablist" aria-label="Stundenplaneinstellungen">${REITER.map(([r, t]) => `<button type="button" role="tab" aria-selected="${r === reiter}" class="${r === reiter ? 'an' : ''}" data-reiter="${r}">${t}</button>`).join('')}</nav>
      <div class="pe-inhalt" role="tabpanel">${{ formate, sichtbarkeit, ical }[reiter]?.() ?? allgemein()}</div>
      ${bearbeiten ? editor(bearbeiten) : ''}</div>`;
    const rumpf = seite.querySelector('.pe-panel-rumpf');
    if (rumpf) rumpf.scrollTop = scroll;
    binden();
  };

  const zuEditor = f => { bearbeiten = f; zeichnen(); seite.querySelector('.pe-panel [data-f="name"]')?.focus(); };

  const binden = () => {
    seite.querySelectorAll('[data-reiter]').forEach(b => b.onclick = () => { reiter = b.dataset.reiter; history.replaceState(null, '', `#/planeinstellungen/${reiter}`); zeichnen(); });
    // Allgemein
    seite.querySelectorAll('[data-zuordnung]').forEach(s => s.onchange = () => { zuordnung[s.dataset.zuordnung] = s.value ? Number(s.value) : null; zeichnen(); });
    const v = seite.querySelector('[data-videolinks]');
    if (v) v.onchange = () => { zuordnung.videoLinks = v.checked; zeichnen(); };
    seite.querySelector('[data-zuordnung-speichern]')?.addEventListener('click', async ev => {
      const knopf = ev.currentTarget;
      knopf.disabled = true;
      try {
        d.zuordnung = await senden('/api/app/admin/planformate-zuordnung', { ...zuordnung, kollegium: true }, 'PUT');
        zuordnung = structuredClone(d.zuordnung);
        toast('Einstellungen gespeichert.');
        zeichnen();
      } catch (err) { toast(err.message, true); knopf.disabled = false; }
    });
    // Liste
    seite.querySelector('[data-neu]')?.addEventListener('click', () => zuEditor(neuesFormat()));
    seite.querySelector('[data-sortieren]')?.addEventListener('click', () => { absteigend = !absteigend; zeichnen(); });
    seite.querySelectorAll('[data-bearbeiten]').forEach(z => {
      const oeffnen = () => {
        const f = structuredClone(d.formate.find(x => x.id === Number(z.dataset.bearbeiten)));
        if (!f.tage?.length) f.tage = [...ALLE_TAGE];
        zuEditor(f);
      };
      z.onclick = oeffnen;
      z.onkeydown = ev => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); oeffnen(); } };
    });
    if (!bearbeiten) return;

    // Panel
    const f = bearbeiten;
    const schliessen = () => { bearbeiten = null; zeichnen(); };
    seite.querySelectorAll('[data-abbrechen], [data-schliessen]').forEach(b => b.onclick = schliessen);
    const panel = seite.querySelector('.pe-panel');
    panel.onkeydown = ev => { if (ev.key === 'Escape') schliessen(); };
    seite.querySelectorAll('.pe-panel [data-f]').forEach(x => {
      const lesen = () => { f[x.dataset.f] = x.type === 'checkbox' ? x.checked : x.value; };
      x.addEventListener('change', lesen);
      if (x.type !== 'checkbox') x.addEventListener('input', lesen);
    });
    seite.querySelectorAll('[data-raster]').forEach(r => r.onchange = () => { if (r.checked) f.zeitraster = r.value; });
    seite.querySelector('[data-alletage]').onchange = ev => { f.tage = ev.target.checked ? [...ALLE_TAGE] : [0, 1, 2, 3, 4]; zeichnen(); };
    seite.querySelectorAll('[data-tag]').forEach(b => b.onclick = () => {
      const t = Number(b.dataset.tag);
      if (f.tage.includes(t) && f.tage.length === 1) return;
      f.tage = f.tage.includes(t) ? f.tage.filter(x => x !== t) : [...f.tage, t].sort((a, c) => a - c);
      b.classList.toggle('an', f.tage.includes(t));
      b.setAttribute('aria-pressed', String(f.tage.includes(t)));
    });
    seite.querySelectorAll('[data-element]').forEach(z => {
      const i = Number(z.dataset.element);
      z.querySelector('[data-e-art]').onchange = ev => { f.elemente[i].art = ev.target.value; zeichnen(); };
      const n = z.querySelector('[data-e-name]');
      if (n) n.onchange = ev => { f.elemente[i].name = ev.target.value; };
      // Ziehen zum Sortieren (am Griff)
      z.ondragstart = ev => { ziehen = i; z.classList.add('zieht'); ev.dataTransfer.effectAllowed = 'move'; ev.dataTransfer.setData('text/plain', String(i)); };
      z.ondragend = () => { ziehen = null; z.classList.remove('zieht'); };
      z.ondragover = ev => { if (ziehen === null) return; ev.preventDefault(); z.classList.add('ziel'); };
      z.ondragleave = () => z.classList.remove('ziel');
      z.ondrop = ev => {
        ev.preventDefault();
        if (ziehen === null || ziehen === i) return;
        const [x] = f.elemente.splice(ziehen, 1);
        f.elemente.splice(i, 0, x);
        ziehen = null;
        zeichnen();
      };
    });
    seite.querySelectorAll('[data-e-weg]').forEach(b => b.onclick = () => { f.elemente.splice(Number(b.dataset.eWeg), 1); zeichnen(); });
    seite.querySelector('[data-e-neu]')?.addEventListener('click', () => {
      const frei = Object.keys(ARTEN).find(a => !f.elemente.some(x => x.art === a)) ?? 'notiz';
      f.elemente.push({ art: frei, name: 'kurz' });
      zeichnen();
    });
    seite.querySelector('[data-format-loeschen]')?.addEventListener('click', async () => {
      if (!await bestaetigen('Format löschen', `Stundenplanformat „${f.name}“ löschen? Pläne, die es verwenden, erscheinen wieder in der Standarddarstellung.`, 'Löschen')) return;
      try { await api(`${BASIS}/${f.id}`, { methode: 'DELETE' }); d = await holen(BASIS); zuordnung = structuredClone(d.zuordnung); bearbeiten = null; toast('Format gelöscht.'); zeichnen(); } catch (err) { toast(err.message, true); }
    });
    seite.querySelector('[data-format-speichern]').onclick = async ev => {
      const knopf = ev.currentTarget;
      knopf.disabled = true;
      try {
        const daten = { ...f, tage: ALLE_TAGE.every(t => f.tage.includes(t)) ? [] : f.tage };
        await senden(f.id ? `${BASIS}/${f.id}` : BASIS, daten, f.id ? 'PUT' : 'POST');
        d = await holen(BASIS);
        zuordnung = structuredClone(d.zuordnung);
        bearbeiten = null;
        reiter = 'formate';
        toast('Stundenplanformat gespeichert.');
        zeichnen();
      } catch (err) { toast(err.message, true); knopf.disabled = false; }
    };
  };

  seite.innerHTML = lade();
  zeichnen();
}
