// Unterricht → Schülergruppen (Leitfaden Klassenleitung, Kapitel 2 und 3): Lehrkräfte bzw. Klassenleitungen ordnen Schüler*innen den
// Schülergruppen zu. Drei Wege führen zur Zuordnung: Mein Unterricht → Personensymbol in der Spalte „Schülergruppen“, Schülergruppen →
// Personensymbol in der Spalte „Schüler“ bzw. Nummer in der Spalte „Unterricht“. Dort öffnet das Personensymbol die Schülerliste mit
// Auswahl, Von/Bis, Zuordnungshistorie, Umkehrungssymbol, Geschlechtsauswahl und Zwischenablage. Bereichs- und Klassenleitungen ordnen
// über die Schüler-Gruppen-Zuordnung (Matrix) zu: Auswahlkriterien Klasse, Schüler*innen, Fach (additiv), je Zelle Häkchen und – über das
// Kalendersymbol – Zeiträume für den Gruppenwechsel im laufenden Schuljahr. Häkchen entfernen löscht die Zugehörigkeit ganz (Warnung).
// Adressen: #/schuelergruppen/<Klasse>/<Schüler-Id | ->/<Fach | ->, #/schuelergruppen/matrix/<Klassen>/<Schüler-Ids>/<Fächer>
import { e, holen, senden, api, toast, leer } from '../ui.js';
import { kbDialog as dialog, kbBestaetigen as bestaetigen } from '../kbdialog.js';
import { elementWahl } from '../elementwahl.js';

const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';
const zeitraumText = z => `${z.von ? datumDe(z.von) : '…'} - ${z.bis ? datumDe(z.bis) : '…'}`;
const GESCHLECHT = { m: ['gender-male', 'männlich'], w: ['gender-female', 'weiblich'], d: ['gender-ambiguous', 'divers'] };
const geschlecht = g => GESCHLECHT[g] ? `<i class="bi bi-${GESCHLECHT[g][0]} sg-g-${g}" title="${GESCHLECHT[g][1]}" aria-label="${GESCHLECHT[g][1]}"></i>` : '';
const ABLAGE = 'untplan:schuelergruppen-ablage';
const WARNUNG = 'Wird das Häkchen entfernt, gehört die Schülerin bzw. der Schüler im gesamten Schuljahr nicht mehr zu dieser Gruppe. '
  + 'Abwesenheitseinträge und Entschuldigungen in dieser Gruppe gehen dabei verloren. Tragen Sie bei einem Gruppenwechsel stattdessen den Zeitraum der Zugehörigkeit '
  + '(Start- und Enddatum) ein. Häkchen trotzdem entfernen?';

/** Zwischenablage der Schülerauswahl (über Gruppen hinweg, für Gruppe A → Gruppe B mit Umkehrung). */
const ablage = {
  lesen() { try { return JSON.parse(sessionStorage.getItem(ABLAGE) ?? 'null'); } catch { return null; } },
  schreiben(ids) { try { sessionStorage.setItem(ABLAGE, JSON.stringify(ids)); } catch { /* privat */ } },
};

export async function zeigen(seite, kontext, teile) {
  if (teile[0] === 'matrix') return matrix(seite, kontext, teile.slice(1));
  return liste(seite, kontext, teile);
}

// ------------------------------------------------------------------ Unterricht → Schülergruppen (Liste)

async function liste(seite, kontext, [klasseParam, schuelerParam, fachParam]) {
  const el = await kontext.elemente();
  // Vorbelegung: die eigene Klasse (Klassenleitung), sonst die erste
  const eigene = el.klassen.find(k => kontext.ich?.element && (k.leitung ?? []).some(l => l.toLowerCase() === kontext.ich.element.toLowerCase()))?.kurz;
  const klasse = (klasseParam && klasseParam !== '-' ? klasseParam : '') || eigene || el.klassen[0]?.kurz || '';
  const schueler = schuelerParam && schuelerParam !== '-' ? Number(schuelerParam) : null;
  const fach = fachParam && fachParam !== '-' ? fachParam : '';
  const ziel = (k = klasse, s = schueler, f = fach) => `#/schuelergruppen/${encodeURIComponent(k || '-')}/${s ?? '-'}/${encodeURIComponent(f || '-')}`;
  kontext.titel('Schülergruppen', `<a class="btn btn-outline-secondary" href="#/schuelergruppen/matrix/${encodeURIComponent(klasse || '-')}/-/-">Schüler-Gruppen-Zuordnung</a>`);
  seite.innerHTML = `<div class="nl-leiste sg-leiste">
      <label class="sg-feld"><span>Klasse</span><span data-wahl></span></label>
      <label class="sg-feld"><span>Schüler*innen</span><select class="form-select" data-schueler><option value="">- Alle -</option></select></label>
      <label class="sg-feld"><span>Fach</span><select class="form-select" data-fach><option value="">- Alle -</option></select></label>
      <label class="nl-suche ms-auto"><i class="bi bi-search" aria-hidden="true"></i><input type="search" data-suche placeholder="Suchen" aria-label="Schülergruppen suchen"></label>
    </div>
    <div class="nl-tabelle sg-tabelle" data-inhalt></div>`;
  seite.querySelector('[data-wahl]').replaceWith(elementWahl({ eintraege: el.klassen, wert: klasse, platzhalter: 'Klasse wählen', beiWahl: x => kontext.navigieren(ziel(x, null, '')) }));
  const inhalt = seite.querySelector('[data-inhalt]');
  if (!klasse) { inhalt.innerHTML = `<div class="p-3">${leer('Bitte eine Klasse wählen.', 'people')}</div>`; return; }
  const q = new URLSearchParams({ klasse });
  if (schueler) q.set('schueler', schueler);
  if (fach) q.set('fach', fach);
  const d = await holen(`/api/app/schuelergruppen?${q}`);
  const sSel = seite.querySelector('[data-schueler]');
  sSel.insertAdjacentHTML('beforeend', d.schueler.map(x => `<option value="${x.id}" ${x.id === schueler ? 'selected' : ''}>${e(x.name)} ${e(x.vorname)}</option>`).join(''));
  sSel.onchange = () => kontext.navigieren(ziel(klasse, sSel.value ? Number(sSel.value) : null, fach));
  const fSel = seite.querySelector('[data-fach]');
  fSel.insertAdjacentHTML('beforeend', d.faecher.map(f => `<option ${f === fach ? 'selected' : ''}>${e(f)}</option>`).join(''));
  fSel.onchange = () => kontext.navigieren(ziel(klasse, schueler, fSel.value));
  const neu = () => kontext.navigieren(location.hash);
  const gewaehlt = new Set();
  const zeichnen = () => {
    inhalt.innerHTML = d.gruppen.length ? `<table><thead><tr>
        <th class="nl-wahl"><span class="sg-kopfwahl">Auswahl<input type="checkbox" class="nl-rund" data-alle aria-label="Alle auswählen"></span></th><th></th>
        <th>Name</th><th>Klassen</th><th>Fach</th><th>Von</th><th>Bis</th><th class="text-center">Schüler</th><th class="text-end">Unterricht</th></tr></thead><tbody>
      ${d.gruppen.map((g, i) => `<tr data-i="${i}" data-text="${e(`${g.name} ${g.klassen.join(' ')} ${g.fach} ${g.lehrer.join(' ')} ${g.unterricht}`.toLowerCase())}">
        <td class="nl-wahl"><input type="checkbox" class="nl-rund" data-wahl="${i}" ${gewaehlt.has(i) ? 'checked' : ''} ${g.bearbeiten ? '' : 'disabled'} aria-label="${e(g.name)} auswählen"></td>
        <td class="text-nowrap">${g.bearbeiten ? `<button type="button" class="nl-icon sichtbar" data-aendern="${i}" title="Bearbeiten" aria-label="Bearbeiten"><i class="bi bi-pencil"></i></button>
          ${!g.standard || g.id ? `<button type="button" class="nl-icon sichtbar rot" data-loeschen="${i}" title="${g.standard ? 'Abweichungen zurücksetzen' : 'Löschen'}" aria-label="${g.standard ? 'Abweichungen zurücksetzen' : 'Löschen'}"><i class="bi bi-x-lg"></i></button>` : ''}` : ''}</td>
        <td><span class="ug-name">${e(g.name)}</span>${g.gast ? ' <span class="nl-chip lila">Gast</span>' : ''}</td>
        <td>${e(g.klassen.join(', '))}</td><td>${e(g.fach)}</td>
        <td class="text-nowrap">${datumDe(g.von)}</td><td class="text-nowrap">${datumDe(g.bis)}</td>
        <td class="text-center"><button type="button" class="sg-person" data-schueler-knopf="${i}" title="Schüler*innen im Unterricht" aria-label="Schüler*innen im Unterricht"><i class="bi bi-person-fill"></i><small>${g.schueler}</small></button></td>
        <td class="text-end"><button type="button" class="sg-nummer" data-unterricht="${i}" title="Schülergruppen des Unterrichts ${g.unterricht}">${g.unterricht}</button></td></tr>`).join('')}
      </tbody></table>
      <div class="sg-fuss"><span>${d.gruppen.length === 1 ? 'Ein Element gefunden.' : `${d.gruppen.length} Elemente gefunden, Anzeige aller Elemente.`}</span>
        <button type="button" class="nl-knopf rot" data-sammel ${gewaehlt.size ? '' : 'disabled'}><i class="bi bi-trash"></i>Löschen</button></div>`
      : `<div class="p-3">${leer(schueler || fach ? 'Keine Schülergruppen für diese Auswahl.' : 'Keine Unterrichte mit Schülergruppen in dieser Klasse.', 'people')}</div>`;
    inhalt.querySelectorAll('[data-wahl]').forEach(c => c.onchange = () => { const i = Number(c.dataset.wahl); if (c.checked) gewaehlt.add(i); else gewaehlt.delete(i); inhalt.querySelector('[data-sammel]').disabled = !gewaehlt.size; });
    inhalt.querySelector('[data-alle]')?.addEventListener('change', ev => { d.gruppen.forEach((g, i) => { if (g.bearbeiten) { if (ev.target.checked) gewaehlt.add(i); else gewaehlt.delete(i); } }); zeichnen(); });
    inhalt.querySelectorAll('[data-schueler-knopf], [data-unterricht]').forEach(b => b.onclick = () => zuordnungDialog(d.gruppen[Number(b.dataset.schuelerKnopf ?? b.dataset.unterricht)].unterricht, neu));
    inhalt.querySelectorAll('[data-aendern]').forEach(b => b.onclick = () => gruppeBearbeiten(d.gruppen[Number(b.dataset.aendern)], neu));
    inhalt.querySelectorAll('[data-loeschen]').forEach(b => b.onclick = () => gruppenLoeschen([d.gruppen[Number(b.dataset.loeschen)]], neu));
    inhalt.querySelector('[data-sammel]')?.addEventListener('click', () => gruppenLoeschen([...gewaehlt].map(i => d.gruppen[i]).filter(g => !g.standard || g.id), neu));
  };
  zeichnen();
  seite.querySelector('[data-suche]').oninput = ev => {
    const t = ev.target.value.trim().toLowerCase();
    inhalt.querySelectorAll('tbody tr').forEach(tr => { tr.hidden = t.length > 0 && !tr.dataset.text.includes(t); });
  };
}

function gruppeBearbeiten(g, nachher, neuFuer = null) {
  const standard = !neuFuer && g.standard;
  dialog({
    titel: neuFuer ? 'Neue Schülergruppe' : `Schülergruppe ${g.name}`,
    inhalt: `<div class="sg-formular">
      <label class="sg-dfeld"><span>Name</span><input class="form-control" name="name" maxlength="100" value="${e(g?.name ?? '')}" required></label>
      ${standard ? ''
        : `<div class="sg-reihe"><label class="sg-dfeld"><span>Von</span><input type="date" class="form-control" name="von" value="${g?.gast || !neuFuer ? g?.von ?? '' : ''}"></label>
           <label class="sg-dfeld"><span>Bis</span><input type="date" class="form-control" name="bis" value="${g?.gast || !neuFuer ? g?.bis ?? '' : ''}"></label></div>`}
    </div>`,
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
      const w = n => box.querySelector(`[name=${n}]`)?.value ?? '';
      if (!w('name').trim()) { toast('Bitte einen Namen eingeben.', true); return false; }
      if (neuFuer) {
        const r = await senden(`/api/app/unterrichtklassen/${neuFuer}/gruppen`, { name: w('name'), von: w('von'), bis: w('bis') });
        toast('Schülergruppe angelegt.');
        nachher?.(r);
        return;
      }
      await senden(`/api/app/unterrichtklassen/${g.unterricht}/gruppen/${g.id}`, { name: w('name'), von: w('von'), bis: w('bis') }, 'PUT');
      toast('Gespeichert.');
      nachher?.();
    } }],
  });
}

async function gruppenLoeschen(gruppen, nachher) {
  if (!gruppen.length) return;
  const text = gruppen.length === 1
    ? (gruppen[0].standard ? `Alle Abweichungen der Schülergruppe „${gruppen[0].name}“ zurücksetzen? Danach nehmen wieder alle Schüler*innen der Klassen teil.`
      : `Schülergruppe „${gruppen[0].name}“ löschen? Ihre Schüler*innen erscheinen dann nicht mehr im Klassenbuch dieses Unterrichts.`)
    : `${gruppen.length} Schülergruppen löschen bzw. (Standardgruppen) zurücksetzen?`;
  if (!await bestaetigen('Schülergruppe', text, 'Löschen')) return;
  try {
    for (const g of gruppen) await api(`/api/app/unterrichtklassen/${g.unterricht}/gruppen/${g.id}`, { methode: 'DELETE' });
    toast('Gelöscht.');
  } catch (err) { toast(err.message, true); }
  nachher?.();
}

// ------------------------------------------------------------------ Schülergruppen eines Unterrichts („Zugeordnet“)

/**
 * Schülergruppen eines Unterrichts wie im Leitfaden: Kopf „Lehrkraft: …, Fach: …, Klasse: …“, Tabelle Zugeordnet | Name | Klassen | Fach |
 * Von | Bis | Schüler; „Neu“ legt eine weitere Schülergruppe an, „Zurück“ schließt.
 */
export async function zuordnungDialog(unterricht, nachher) {
  const d = await holen(`/api/app/unterrichtklassen/${unterricht}/gruppen`);
  let geaendert = false;
  const box = dialog({
    titel: 'Schülergruppen', breit: true,
    inhalt: `<div class="ug-dialog">
      <p class="sg-unterkopf">Lehrkraft: ${e(d.lehrer.join(', ') || '–')}, Fach: ${e(d.fach)}, Klasse: ${e(d.klassen.join(', '))}</p>
      <div class="nl-tabelle"><table><thead><tr><th class="nl-wahl"><span class="sg-kopfwahl">Zugeordnet<input type="checkbox" class="nl-rund" checked disabled aria-label="Zugeordnet"></span></th><th></th>
        <th>Name</th><th>Klassen</th><th>Fach</th><th>Von</th><th>Bis</th><th class="text-center">Schüler</th></tr></thead><tbody>
      ${d.gruppen.map(g => `<tr>
        <td class="nl-wahl"><input type="checkbox" class="nl-rund" checked disabled aria-label="zugeordnet"></td>
        <td>${d.schreiben ? `<button type="button" class="nl-icon sichtbar" data-aendern="${g.id}" title="Bearbeiten" aria-label="Bearbeiten"><i class="bi bi-pencil"></i></button>` : ''}</td>
        <td><span class="ug-name">${e(g.name)}</span>${g.gast ? ' <span class="nl-chip lila">Gast</span>' : ''}</td>
        <td>${e(d.klassen.join(', '))}</td><td>${e(d.fach)}</td>
        <td class="text-nowrap">${datumDe(g.von)}</td><td class="text-nowrap">${datumDe(g.bis)}</td>
        <td class="text-center"><button type="button" class="sg-person" data-mitglieder="${g.id}" title="Schüler*innen im Unterricht" aria-label="Schüler*innen im Unterricht"><i class="bi bi-person-fill"></i><small>${g.anzahl}</small></button></td></tr>`).join('')}
      </tbody></table></div></div>`,
    knoepfe: [...(d.schreiben ? [{ text: 'Neu', aktion: () => { gruppeBearbeiten(null, () => { geaendert = true; zuordnungDialog(unterricht, nachher); }, unterricht); } }] : []), { text: 'Zurück' }],
  });
  box.geschlossen.then(() => { if (geaendert) nachher?.(); });
  box.querySelectorAll('[data-mitglieder]').forEach(b => b.onclick = () => schuelerDialog(unterricht, Number(b.dataset.mitglieder), () => { geaendert = true; }));
  box.querySelectorAll('[data-aendern]').forEach(b => b.onclick = () => {
    const g = d.gruppen.find(x => x.id === Number(b.dataset.aendern));
    gruppeBearbeiten({ ...g, unterricht, standard: g.art === 'standard' }, () => { geaendert = true; box.schliessen(); setTimeout(() => zuordnungDialog(unterricht, nachher), 350); });
  });
}

// ------------------------------------------------------------------ Schüler*innen im Unterricht (Schülerliste der Gruppe)

/**
 * Schülerliste einer Schülergruppe: Auswahl (Häkchen), Nr, Familienname, Vorname, Geschlecht, Klasse, Von, Bis, Kurzname, Externe Id,
 * Zuordnungshistorie; Werkzeuge Rückgängig, Umkehren, Schüler, Schülerinnen, alle, In die Zwischenablage, Aus der Zwischenablage.
 * Gruppenwechsel im laufenden Schuljahr: Start- und Endedatum eintragen, die Häkchen nicht ändern – nach dem Speichern steht der
 * abgelaufene Zeitraum in der Zuordnungshistorie.
 */
export async function schuelerDialog(unterricht, gruppe, nachher, stichtag = '') {
  const d = await holen(`/api/app/unterrichtklassen/${unterricht}/gruppen/${gruppe}/schueler${stichtag ? `?datum=${stichtag}` : ''}`);
  const anfang = d.schueler.map(x => ({ ...x }));
  let zeilen = anfang.map(x => ({ ...x }));
  const nurLesen = !d.schreiben;
  const zeile = (x, i) => `<tr data-i="${i}" class="${x.auswahl ? '' : 'ug-aus'}">
      <td class="nl-wahl"><input type="checkbox" class="nl-rund" data-auswahl ${x.auswahl ? 'checked' : ''} ${nurLesen ? 'disabled' : ''} aria-label="${e(`${x.vorname} ${x.name}`)} auswählen"></td>
      <td class="ug-nr">${i + 1}</td>
      <td class="ug-name">${e(x.name)}</td><td>${e(x.vorname)}</td>
      <td class="text-center">${geschlecht(x.geschlecht)}</td><td>${e(x.klasse)}</td>
      <td><input type="date" data-von value="${x.von}" ${nurLesen ? 'disabled' : ''} aria-label="Von"></td>
      <td><input type="date" data-bis value="${x.bis}" ${nurLesen ? 'disabled' : ''} aria-label="Bis"></td>
      <td class="nl-unter">${e(x.kurzname)}</td><td class="nl-unter sg-extern">${e(x.externeId)}</td>
      <td class="nl-unter text-nowrap">${x.historie.map(zeitraumText).join('<br>')}</td></tr>`;
  const werkzeug = (attr, symbol, titel) => `<button type="button" class="sg-werkzeug" ${attr} title="${titel}" aria-label="${titel}" ${nurLesen ? 'disabled' : ''}><i class="bi bi-${symbol}"></i></button>`;
  let geaendert = false;
  const box = dialog({
    titel: `Schüler*innen in der Schülergruppe ${d.gruppe.name}`, breite: 'xl',
    inhalt: `<div class="ug-dialog sg-schueler">
      <div class="sg-unterkopf">Klasse: ${e(d.klassen.join(', '))}<br>Schülerzahl: <span data-zahl></span></div>
      <div class="sg-werkzeuge" role="toolbar" aria-label="Auswahl">
        ${werkzeug('data-rueck', 'arrow-counterclockwise', 'Rückgängig (Stand beim Öffnen)')}
        ${werkzeug('data-umkehren', 'grid-3x3-gap', 'Auswahl umkehren')}
        ${werkzeug('data-g="m"', 'gender-male', 'Schüler auswählen')}
        ${werkzeug('data-g="w"', 'gender-female', 'Schülerinnen auswählen')}
        ${werkzeug('data-g="*"', 'gender-ambiguous', 'Alle auswählen')}
        <span class="sg-trenn"></span>
        ${werkzeug('data-kopieren', 'copy', 'Auswahl in die Zwischenablage kopieren')}
        ${werkzeug('data-einfuegen', 'clipboard-check', 'Auswahl aus der Zwischenablage holen')}
        <label class="nl-suche ms-auto"><i class="bi bi-search" aria-hidden="true"></i><input type="search" data-suche placeholder="Name suchen" aria-label="Name suchen"></label>
      </div>
      <div class="nl-tabelle sg-schuelertabelle" data-tabelle></div></div>`,
    knoepfe: nurLesen ? [{ text: 'Schließen' }] : [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async () => {
      for (const x of zeilen) if (x.von && x.bis && x.von > x.bis) throw new Error(`${x.vorname} ${x.name}: „Von“ liegt nach „Bis“.`);
      await senden(`/api/app/unterrichtklassen/${unterricht}/gruppen/${gruppe}/schueler${stichtag ? `?datum=${stichtag}` : ''}`, {
        schueler: zeilen.map(x => ({ schuelerId: x.id, auswahl: x.auswahl, zeitraeume: [...x.historie, ...(x.auswahl ? [{ von: x.von, bis: x.bis }] : [])] })),
      }, 'PUT');
      toast('Schülergruppe gespeichert.');
      geaendert = true;
    } }],
  });
  box.geschlossen.then(() => { if (geaendert) nachher?.(); });
  const tabelle = box.querySelector('[data-tabelle]');
  const zeichnen = () => {
    tabelle.innerHTML = zeilen.length ? `<table><thead><tr><th class="nl-wahl"><span class="sg-kopfwahl">Auswahl<input type="checkbox" class="nl-rund" data-alle ${zeilen.every(x => x.auswahl) ? 'checked' : ''} ${nurLesen ? 'disabled' : ''} aria-label="Alle auswählen"></span></th>
        <th></th><th>Familienname</th><th>Vorname</th><th class="text-center">Geschlecht</th><th>Klasse</th><th>Von</th><th>Bis</th><th>Kurzname</th><th>Externe Id</th><th>Zuordnungshistorie</th></tr></thead>
      <tbody>${zeilen.map(zeile).join('')}</tbody></table>` : leer('Keine Schüler*innen in dieser Gruppe.', 'people');
    box.querySelector('[data-zahl]').textContent = zeilen.filter(x => x.auswahl).length;
    tabelle.querySelectorAll('tbody tr[data-i]').forEach(tr => {
      const x = zeilen[Number(tr.dataset.i)];
      tr.querySelector('[data-auswahl]').onchange = async ev => {
        // Warnung beim Entfernen eines Häkchens ohne Zeitraum (Zugehörigkeit ginge ganz verloren)
        if (!ev.target.checked && x.auswahl && !x.von && !x.bis && !x.historie.length && d.gruppe.standard) {
          ev.target.checked = true;
          if (!await bestaetigen('Häkchen entfernen', WARNUNG, 'Entfernen')) return;
          ev.target.checked = false;
        }
        x.auswahl = ev.target.checked;
        tr.classList.toggle('ug-aus', !x.auswahl);
        box.querySelector('[data-zahl]').textContent = zeilen.filter(y => y.auswahl).length;
      };
      tr.querySelector('[data-von]').onchange = ev => { x.von = ev.target.value; };
      tr.querySelector('[data-bis]').onchange = ev => { x.bis = ev.target.value; };
    });
    tabelle.querySelector('[data-alle]')?.addEventListener('change', ev => { setzen(() => ev.target.checked); });
  };
  const setzen = f => { zeilen.forEach(x => { x.auswahl = f(x); }); zeichnen(); };
  zeichnen();
  box.querySelector('[data-rueck]').onclick = () => { zeilen = anfang.map(x => ({ ...x })); zeichnen(); };
  box.querySelector('[data-umkehren]').onclick = () => setzen(x => !x.auswahl);
  box.querySelectorAll('[data-g]').forEach(b => b.onclick = () => setzen(x => b.dataset.g === '*' || x.geschlecht === b.dataset.g));
  box.querySelector('[data-kopieren]').onclick = () => { ablage.schreiben(zeilen.filter(x => x.auswahl).map(x => x.id)); toast(`${zeilen.filter(x => x.auswahl).length} Schüler*innen in der Zwischenablage.`); };
  box.querySelector('[data-einfuegen]').onclick = () => {
    const ids = ablage.lesen();
    if (!Array.isArray(ids)) { toast('Die Zwischenablage ist leer.', true); return; }
    setzen(x => ids.includes(x.id));
  };
  box.querySelector('[data-suche]').oninput = ev => {
    const t = ev.target.value.trim().toLowerCase();
    tabelle.querySelectorAll('tbody tr[data-i]').forEach(tr => { const x = zeilen[Number(tr.dataset.i)]; tr.hidden = t.length > 0 && !`${x.name} ${x.vorname} ${x.kurzname}`.toLowerCase().includes(t); });
  };
}

// ------------------------------------------------------------------ Schüler-Gruppen-Zuordnung (Matrix)

const listeAus = p => p && p !== '-' ? decodeURIComponent(p).split(',').filter(Boolean) : [];

async function matrix(seite, kontext, [klassenParam, schuelerParam, faecherParam]) {
  const el = await kontext.elemente();
  const krit = { klassen: listeAus(klassenParam), schueler: listeAus(schuelerParam).map(Number), faecher: listeAus(faecherParam) };
  const ziel = k => `#/schuelergruppen/matrix/${encodeURIComponent(k.klassen.join(',') || '-')}/${k.schueler.join(',') || '-'}/${encodeURIComponent(k.faecher.join(',') || '-')}`;
  kontext.titel('Schüler-Gruppen-Zuordnung');
  const q = new URLSearchParams({ klassen: krit.klassen.join(','), schueler: krit.schueler.join(','), faecher: krit.faecher.join(',') });
  const d = await holen(`/api/app/schuelergruppen/matrix?${q}`);
  const aenderungen = new Map();
  const zelle = (r, c) => d.zeilen[r].zellen[c];
  const schluessel = (r, c) => `${r}:${c}`;
  let aktiveSpalte = null;
  seite.innerHTML = `<div class="sg-matrixseite">
      <div class="nl-leiste sg-kriterien">
        <div class="sg-kriterien-wahl"><button type="button" class="nl-knopf" data-kriterien aria-expanded="false">Auswahlkriterien <i class="bi bi-caret-down-fill"></i></button>
          <div class="sg-pop sg-kriterien-pop" data-kriterien-pop hidden>
            <div class="sg-pop-kopf"><b>Auswahlkriterien</b><button type="button" class="btn-close" data-pop-zu aria-label="Schließen"></button></div>
            <label class="sg-dfeld"><span>Klasse</span><select class="form-select" data-k-klasse><option value=""></option>${el.klassen.filter(k => !krit.klassen.includes(k.kurz)).map(k => `<option>${e(k.kurz)}</option>`).join('')}</select></label>
            <label class="sg-dfeld"><span>Schüler*innen</span><select class="form-select" data-k-schueler><option value=""></option></select></label>
            <label class="sg-dfeld"><span>Fach</span><select class="form-select" data-k-fach><option value=""></option>${(el.faecher ?? []).filter(f => !krit.faecher.includes(f.kurz)).map(f => `<option>${e(f.kurz)}</option>`).join('')}</select></label>
          </div></div>
        <div class="sg-chips">${[...krit.klassen.map(k => ['klassen', k, k]), ...d.kriterien.schueler.map(s => ['schueler', s.id, s.name]), ...krit.faecher.map(f => ['faecher', f, f])]
          .map(([art, wert, text]) => `<span class="sg-chip">${e(text)}<button type="button" data-weg="${art}" data-wert="${e(wert)}" aria-label="${e(text)} entfernen"><i class="bi bi-x-lg"></i></button></span>`).join('')}</div>
      </div>
      <div class="sg-matrix" data-matrix></div>
      <div class="sg-matrixfuss"><span data-stand></span><button type="button" class="btn btn-outline-secondary" data-abbrechen>Abbrechen</button><button type="button" class="btn btn-primary" data-speichern disabled>Speichern</button></div>
    </div>`;

  // Auswahlkriterien (additiv): Klasse, Schüler*innen (aus den Klassen der Matrix bzw. allen Klassen), Fach
  const pop = seite.querySelector('[data-kriterien-pop]');
  const kKnopf = seite.querySelector('[data-kriterien]');
  const popZu = () => { pop.hidden = true; kKnopf.setAttribute('aria-expanded', 'false'); };
  kKnopf.onclick = async () => {
    pop.hidden = !pop.hidden;
    kKnopf.setAttribute('aria-expanded', String(!pop.hidden));
    const sSel = pop.querySelector('[data-k-schueler]');
    if (!pop.hidden && sSel.options.length <= 1) {
      const quelle = d.zeilen.length ? d.zeilen : [];
      sSel.insertAdjacentHTML('beforeend', quelle.filter(x => !krit.schueler.includes(x.id)).map(x => `<option value="${x.id}">${e(x.name)} ${e(x.vorname)} (${e(x.klasse)})</option>`).join(''));
    }
  };
  pop.querySelector('[data-pop-zu]').onclick = popZu;
  const hinzu = (art, wert) => { if (!wert) return; const k = { ...krit, [art]: [...krit[art], art === 'schueler' ? Number(wert) : wert] }; weiter(k); };
  pop.querySelector('[data-k-klasse]').onchange = ev => hinzu('klassen', ev.target.value);
  pop.querySelector('[data-k-schueler]').onchange = ev => hinzu('schueler', ev.target.value);
  pop.querySelector('[data-k-fach]').onchange = ev => hinzu('faecher', ev.target.value);
  seite.querySelectorAll('[data-weg]').forEach(b => b.onclick = () => {
    const art = b.dataset.weg;
    weiter({ ...krit, [art]: krit[art].filter(x => String(x) !== b.dataset.wert) });
  });
  const weiter = async k => {
    if (aenderungen.size && !await bestaetigen('Schüler-Gruppen-Zuordnung', 'Die Änderungen sind noch nicht gespeichert. Trotzdem fortfahren?', 'Verwerfen')) return;
    aenderungen.clear();
    kontext.navigieren(ziel(k));
  };

  const box = seite.querySelector('[data-matrix]');
  if (!d.spalten.length || !d.zeilen.length) {
    box.innerHTML = `<div class="p-3">${leer(krit.klassen.length + krit.schueler.length + krit.faecher.length ? 'Für diese Auswahlkriterien gibt es keine Schülergruppen.' : 'Bitte über die Auswahlkriterien eine Klasse, Schüler*innen oder ein Fach wählen.', 'people')}</div>`;
    seite.querySelector('.sg-matrixfuss').hidden = true;
    return;
  }
  const stand = () => {
    seite.querySelector('[data-stand]').textContent = aenderungen.size ? `${aenderungen.size} ${aenderungen.size === 1 ? 'Änderung' : 'Änderungen'}` : '';
    seite.querySelector('[data-speichern]').disabled = !aenderungen.size;
  };
  const merken = (r, c) => {
    const z = zelle(r, c);
    aenderungen.set(schluessel(r, c), { unterricht: d.spalten[c].unterricht, gruppe: d.spalten[c].gruppe, schuelerId: d.zeilen[r].id, auswahl: z.a, zeitraeume: z.z });
    stand();
  };
  const zellHtml = (r, c) => {
    const z = zelle(r, c);
    const darf = d.spalten[c].bearbeiten;
    return `<td class="sg-zelle ${z.z.length ? 'befristet' : ''} ${aenderungen.has(schluessel(r, c)) ? 'geaendert' : ''}" data-r="${r}" data-c="${c}">
      <input type="checkbox" class="sg-haken" ${z.a ? 'checked' : ''} ${darf ? '' : 'disabled'} aria-label="${e(`${d.zeilen[r].name} ${d.zeilen[r].vorname} in ${d.spalten[c].name}`)}">
      ${darf || z.z.length ? `<button type="button" class="sg-kal" data-kal title="${z.z.length ? `Gültig: ${z.z.map(zeitraumText).join(', ')}` : 'Zeitraum der Zugehörigkeit'}" aria-label="Zeitraum der Zugehörigkeit"><i class="bi bi-calendar3"></i></button>` : ''}</td>`;
  };
  // Zeilen nach Klassen gruppiert (Klasse senkrecht links)
  const gruppen = [];
  d.zeilen.forEach((x, r) => { const g = gruppen.at(-1); if (g?.klasse === x.klasse) g.r.push(r); else gruppen.push({ klasse: x.klasse, r: [r] }); });
  const werkzeug = (attr, symbol, titel) => `<button type="button" class="sg-werkzeug klein" ${attr} title="${titel}" aria-label="${titel}"><i class="bi bi-${symbol}"></i></button>`;
  box.innerHTML = `<table class="sg-matrixtabelle"><thead><tr>
      <th class="sg-ecke" colspan="2"><div class="sg-eckwerkzeuge">
        ${werkzeug('data-s-alle', 'check2-square', 'Alle der markierten Spalte auswählen')}${werkzeug('data-s-keine', 'square', 'Keine der markierten Spalte auswählen')}${werkzeug('data-s-umkehren', 'grid-3x3-gap', 'Markierte Spalte umkehren')}
        ${werkzeug('data-s-g="m"', 'gender-male', 'Schüler der markierten Spalte auswählen')}${werkzeug('data-s-g="w"', 'gender-female', 'Schülerinnen der markierten Spalte auswählen')}${werkzeug('data-s-kopieren', 'copy', 'Markierte Spalte in die Zwischenablage kopieren')}${werkzeug('data-s-einfuegen', 'clipboard-check', 'Zwischenablage in die markierte Spalte einfügen')}
      </div></th>
      ${d.spalten.map((s, c) => `<th class="sg-spalte" data-spalte="${c}" title="${e(`${s.name}, ${s.fach}, ${s.lehrer.join(', ')}, Unterricht ${s.unterricht}`)}" tabindex="0">
        <span>${e(s.fach)}</span><span>${e(s.lehrer.join(', '))}</span><span class="sg-gname">${e(s.name)}</span></th>`).join('')}</tr></thead>
    <tbody>${gruppen.map(g => g.r.map((r, i) => `<tr>
      ${i === 0 ? `<th class="sg-klasse" rowspan="${g.r.length}"><span>${e(g.klasse)}</span></th>` : ''}
      <th class="sg-name" scope="row">${geschlecht(d.zeilen[r].geschlecht)} ${e(d.zeilen[r].name)} ${e(d.zeilen[r].vorname)}</th>
      ${d.spalten.map((s, c) => zellHtml(r, c)).join('')}</tr>`).join('')).join('')}</tbody></table>`;

  const neuZeichnen = (r, c) => { const td = box.querySelector(`td[data-r="${r}"][data-c="${c}"]`); td.outerHTML = zellHtml(r, c); binden(box.querySelector(`td[data-r="${r}"][data-c="${c}"]`)); };
  const binden = td => {
    const r = Number(td.dataset.r), c = Number(td.dataset.c);
    td.querySelector('.sg-haken').onchange = async ev => {
      const z = zelle(r, c);
      if (!ev.target.checked && !z.z.length) {
        ev.target.checked = true;
        if (!await bestaetigen('Häkchen entfernen', WARNUNG, 'Entfernen')) return;
      }
      z.a = !z.a ? true : false;
      if (!z.a) z.z = [];
      merken(r, c);
      neuZeichnen(r, c);
    };
    td.querySelector('[data-kal]')?.addEventListener('click', ev => { ev.stopPropagation(); kalenderPop(td, r, c); });
  };
  box.querySelectorAll('td.sg-zelle').forEach(binden);

  // Gültigkeitsdaten einer Zelle: Liste Von/Bis (Kalender), Löschen (x), weiterer Zeitraum (+); Klick in die freie Fläche übernimmt
  let offenesPop = null;
  const popSchliessen = () => { offenesPop?.uebernehmen(); offenesPop?.el.remove(); offenesPop = null; };
  function kalenderPop(td, r, c) {
    popSchliessen();
    const z = zelle(r, c);
    const darf = d.spalten[c].bearbeiten;
    const liste = z.z.map(x => ({ ...x }));
    const el = document.createElement('div');
    el.className = 'sg-pop sg-zeitpop';
    const zeichnenPop = () => {
      el.innerHTML = `<div class="sg-zeitkopf"><span>Von</span><span>Bis</span></div>
        ${liste.map((x, i) => `<div class="sg-zeitreihe" data-i="${i}"><input type="date" data-v value="${x.von}" ${darf ? '' : 'disabled'} aria-label="Von"><input type="date" data-b value="${x.bis}" ${darf ? '' : 'disabled'} aria-label="Bis">
          ${darf ? `<button type="button" class="sg-zeitweg" data-weg title="Zeitraum löschen" aria-label="Zeitraum löschen"><i class="bi bi-x-lg"></i></button>` : ''}</div>`).join('')}
        ${darf ? `<div class="sg-zeitreihe neu"><input type="date" data-nv aria-label="Von (neuer Zeitraum)"><input type="date" data-nb aria-label="Bis (neuer Zeitraum)">
          <button type="button" class="sg-zeitplus" data-plus title="Zeitraum hinzufügen" aria-label="Zeitraum hinzufügen"><i class="bi bi-plus-lg"></i></button></div>` : ''}`;
      el.querySelectorAll('.sg-zeitreihe[data-i]').forEach(reihe => {
        const x = liste[Number(reihe.dataset.i)];
        reihe.querySelector('[data-v]').onchange = ev => { x.von = ev.target.value; };
        reihe.querySelector('[data-b]').onchange = ev => { x.bis = ev.target.value; };
        reihe.querySelector('[data-weg]')?.addEventListener('click', () => { liste.splice(Number(reihe.dataset.i), 1); zeichnenPop(); });
      });
      el.querySelector('[data-plus]')?.addEventListener('click', () => {
        const v = el.querySelector('[data-nv]').value, b = el.querySelector('[data-nb]').value;
        if (!v && !b) { toast('Bitte ein Von- und/oder Bis-Datum wählen.', true); return; }
        liste.push({ von: v, bis: b }); zeichnenPop();
      });
    };
    zeichnenPop();
    document.body.append(el);
    const rect = td.getBoundingClientRect();
    el.style.top = `${rect.bottom + window.scrollY + 4}px`;
    el.style.left = `${Math.max(8, Math.min(rect.left + window.scrollX, window.scrollX + document.documentElement.clientWidth - el.offsetWidth - 8))}px`;
    offenesPop = {
      el, uebernehmen() {
        if (!darf) return;
        // neuer, noch nicht mit + übernommener Zeitraum zählt mit
        const v = el.querySelector('[data-nv]')?.value, b = el.querySelector('[data-nb]')?.value;
        if (v || b) liste.push({ von: v, bis: b });
        const neu = liste.filter(x => x.von || x.bis);
        if (neu.some(x => x.von && x.bis && x.von > x.bis)) { toast('„Von“ liegt nach „Bis“. Der Zeitraum wurde nicht übernommen.', true); return; }
        if (JSON.stringify(neu) === JSON.stringify(z.z)) return;
        z.z = neu;
        if (neu.length) z.a = true;
        merken(r, c);
        neuZeichnen(r, c);
      },
    };
  }
  const draussen = ev => { if (offenesPop && !offenesPop.el.contains(ev.target) && !ev.target.closest('.kal-pop, .kal-flaeche, [class*="kal-"]')) popSchliessen(); };
  document.addEventListener('mousedown', draussen);
  // Seite verlassen: Klick-Beobachter und offenes Fenster entfernen
  const beobachter = new MutationObserver(() => { if (!seite.contains(box)) { document.removeEventListener('mousedown', draussen); offenesPop?.el.remove(); beobachter.disconnect(); } });
  beobachter.observe(seite, { childList: true });

  // Spalte markieren und Werkzeuge der Ecke (wirken auf die markierte Spalte)
  const spalteMarkieren = c => {
    aktiveSpalte = aktiveSpalte === c ? null : c;
    box.querySelectorAll('[data-spalte], td.sg-zelle').forEach(x => x.classList.toggle('markiert', aktiveSpalte !== null && Number(x.dataset.spalte ?? x.dataset.c) === aktiveSpalte));
  };
  box.querySelectorAll('[data-spalte]').forEach(th => {
    th.onclick = () => spalteMarkieren(Number(th.dataset.spalte));
    th.onkeydown = ev => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); spalteMarkieren(Number(th.dataset.spalte)); } };
  });
  const spalteSetzen = async f => {
    if (aktiveSpalte === null) { toast('Bitte zuerst eine Spalte (Schülergruppe) anklicken.', true); return; }
    const c = aktiveSpalte;
    if (!d.spalten[c].bearbeiten) { toast('Diese Schülergruppe dürfen Sie nicht bearbeiten.', true); return; }
    const weg = d.zeilen.filter((x, r) => zelle(r, c).a && !f(x, r) && !zelle(r, c).z.length).length;
    if (weg && !await bestaetigen('Häkchen entfernen', `${weg} Häkchen werden entfernt. ${WARNUNG}`, 'Entfernen')) return;
    d.zeilen.forEach((x, r) => {
      const z = zelle(r, c);
      const neu = f(x, r);
      if (neu === z.a) return;
      z.a = neu;
      if (!neu) z.z = [];
      merken(r, c);
      neuZeichnen(r, c);
    });
    box.querySelectorAll(`td.sg-zelle[data-c="${c}"]`).forEach(x => x.classList.add('markiert'));
  };
  box.querySelector('[data-s-alle]').onclick = () => spalteSetzen(() => true);
  box.querySelector('[data-s-keine]').onclick = () => spalteSetzen(() => false);
  box.querySelector('[data-s-umkehren]').onclick = () => spalteSetzen((x, r) => !zelle(r, aktiveSpalte).a);
  box.querySelectorAll('[data-s-g]').forEach(b => b.onclick = () => spalteSetzen(x => x.geschlecht === b.dataset.sG));
  box.querySelector('[data-s-kopieren]').onclick = () => {
    if (aktiveSpalte === null) { toast('Bitte zuerst eine Spalte (Schülergruppe) anklicken.', true); return; }
    const ids = d.zeilen.filter((x, r) => zelle(r, aktiveSpalte).a).map(x => x.id);
    ablage.schreiben(ids);
    toast(`${ids.length} Schüler*innen in der Zwischenablage.`);
  };
  box.querySelector('[data-s-einfuegen]').onclick = () => {
    const ids = ablage.lesen();
    if (!Array.isArray(ids)) { toast('Die Zwischenablage ist leer.', true); return; }
    spalteSetzen(x => ids.includes(x.id));
  };

  seite.querySelector('[data-abbrechen]').onclick = async () => {
    if (aenderungen.size && !await bestaetigen('Schüler-Gruppen-Zuordnung', 'Alle nicht gespeicherten Änderungen verwerfen?', 'Verwerfen')) return;
    aenderungen.clear();
    kontext.navigieren(location.hash);
  };
  seite.querySelector('[data-speichern]').onclick = async ev => {
    popSchliessen();
    ev.currentTarget.disabled = true;
    try {
      const r = await senden('/api/app/schuelergruppen/matrix', { aenderungen: [...aenderungen.values()] }, 'PUT');
      toast(`Gespeichert (${r.anzahl} ${r.anzahl === 1 ? 'Zuordnung' : 'Zuordnungen'}).`);
      aenderungen.clear();
      kontext.navigieren(location.hash);
    } catch (err) { toast(err.message, true); stand(); }
  };
  stand();
}
