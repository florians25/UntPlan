// Unterricht → Unterricht Klassen: Unterrichte einer Klasse im Zeitraum (U-Nr, Klassen, Schülergruppe, Lehrkräfte, Fach, Stunden,
// Termine, Schüler*innen, Schülergruppen, Berichte). Schülergruppen eines Unterrichts mit den Schüler*innen und ihrem Zeitraum
// (Abmeldung vom Unterricht ab einem Datum) und der befristete Unterrichtswechsel als Assistent (Zeitraum → Schüler*innen →
// Zielklasse → Unterrichte). Klassenwechsel ab Datum (Dialog für die Schülerverwaltung) wird von schueler.js aufgerufen.
import { e, holen, senden, api, toast, dialog, bestaetigen, leer, herunterladen, heuteIso, plusTage, montag, initialen } from '../ui.js';
import { elementWahl } from '../elementwahl.js';

const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';
const iso = /^\d{4}-\d{2}-\d{2}$/;
const GESCHLECHT = { m: 'männlich', w: 'weiblich', d: 'divers' };
const BEREICHE = {
  woche: ['Aktuelle Woche', () => [montag(heuteIso()), plusTage(montag(heuteIso()), 6)]],
  naechste: ['Nächste Woche', () => [plusTage(montag(heuteIso()), 7), plusTage(montag(heuteIso()), 13)]],
  monat: ['Aktueller Monat', () => { const h = heuteIso(); const d = new Date(h.slice(0, 7) + '-01T12:00'); d.setMonth(d.getMonth() + 1); d.setDate(0); return [h.slice(0, 8) + '01', h.slice(0, 8) + String(d.getDate()).padStart(2, '0')]; }],
  jahr: ['Schuljahr', null],
};
const csvFeld = x => `"${String(x ?? '').replace(/"/g, '""')}"`;

export async function zeigen(seite, kontext, [klasseParam, bereichParam, vonParam, bisParam]) {
  const el = await kontext.elemente();
  const sj = kontext.schuljahr;
  const klasse = (klasseParam && klasseParam !== '-' ? klasseParam : '') || el.klassen[0]?.kurz || '';
  const bereich = BEREICHE[bereichParam] ? bereichParam : 'woche';
  let [von, bis] = bereich === 'jahr' ? [sj?.von ?? heuteIso(), sj?.bis ?? heuteIso()] : BEREICHE[bereich][1]();
  if (iso.test(vonParam ?? '')) von = vonParam;
  if (iso.test(bisParam ?? '')) bis = bisParam;
  const ziel = (k = klasse, b = bereich, v = von, bi = bis) => `#/unterrichtklassen/${encodeURIComponent(k || '-')}/${b}/${v}/${bi}`;
  const pille = inhalt => `<label class="nl-pille">${inhalt}</label>`;
  kontext.titel(`Unterricht Klasse ${klasse}`);
  seite.innerHTML = `
    <div class="nl-leiste">
      <span data-wahl></span>
      ${pille(`<span class="me-1 text-body-secondary">Von</span><input type="date" data-von value="${von}">`)}
      ${pille(`<span class="me-1 text-body-secondary">Bis</span><input type="date" data-bis value="${bis}">`)}
      ${pille(`<select data-bereich aria-label="Zeitraum">${Object.entries(BEREICHE).map(([k, [t]]) => `<option value="${k}" ${k === bereich && !vonParam ? 'selected' : ''}>${t}</option>`).join('')}${vonParam ? '<option selected>Benutzerdefiniert</option>' : ''}</select>`)}
      <label class="nl-suche"><i class="bi bi-search" aria-hidden="true"></i><input type="search" data-suche placeholder="Suchen" aria-label="Unterricht suchen"></label>
      <span class="ms-auto d-flex flex-wrap gap-2" data-knoepfe></span>
    </div>
    <div class="nl-tabelle" data-inhalt></div>`;
  seite.querySelector('[data-wahl]').replaceWith(elementWahl({ eintraege: el.klassen, wert: klasse, platzhalter: 'Klasse wählen', beiWahl: x => kontext.navigieren(ziel(x)) }));
  seite.querySelector('[data-bereich]').onchange = ev => BEREICHE[ev.target.value] && kontext.navigieren(`#/unterrichtklassen/${encodeURIComponent(klasse || '-')}/${ev.target.value}`);
  seite.querySelectorAll('[data-von], [data-bis]').forEach(x => x.onchange = () => {
    const v = seite.querySelector('[data-von]').value, b = seite.querySelector('[data-bis]').value;
    if (iso.test(v) && iso.test(b)) kontext.navigieren(ziel(klasse, bereich, v, b));
  });
  const inhalt = seite.querySelector('[data-inhalt]');
  if (!klasse) { inhalt.innerHTML = `<div class="p-3">${leer('Bitte eine Klasse wählen.', 'meineunterrichte')}</div>`; return; }

  const d = await holen(`/api/app/unterrichtklassen?klasse=${encodeURIComponent(klasse)}&von=${von}&bis=${bis}`);
  if (d.leitung?.length) kontext.titel(`Unterricht Klasse ${klasse}`, '', `(${d.leitung.join(', ')})`);
  const neu = () => kontext.navigieren(location.hash);
  const knoepfe = seite.querySelector('[data-knoepfe]');
  knoepfe.innerHTML = `${d.schreiben ? '<button type="button" class="nl-knopf" data-wechsel><i class="bi bi-arrow-left-right"></i>Befristeter Unterrichtswechsel</button>' : ''}
    <button type="button" class="nl-knopf" data-bericht><i class="bi bi-filetype-csv"></i>Bericht</button>`;
  const zahlknopf = (attr, symbol, zahl, titel, markiert = false, extra = '') =>
    `<button type="button" class="ug-zahlknopf ${markiert ? 'markiert' : ''}" ${attr} title="${e(titel)}" aria-label="${e(titel)}"><i class="bi bi-${symbol}" aria-hidden="true"></i>${zahl}${extra}</button>`;
  inhalt.innerHTML = d.unterricht.length ? `<table><thead><tr><th>U-Nr</th><th>Klasse(n)</th><th>Schülergruppe</th><th>Lehrkraft</th><th>Fach</th><th class="text-end">Std.</th>
      <th>Termine</th><th>Schüler*innen</th><th>Schülergruppen</th><th class="text-end">Berichte</th></tr></thead><tbody>
    ${d.unterricht.map(u => `<tr data-name="${e([u.id, u.klassen.join(' '), u.gruppe, u.fach, u.fachName, u.lehrer.join(' ')].join(' ').toLowerCase())}">
      <td class="ug-nr">${u.id}</td>
      <td><div class="nl-name">${e(u.klassen.join(', '))}</div></td>
      <td class="nl-unter">${e(u.gruppe)}${u.geaendert ? ' <span class="nl-chip gelb" title="Schülergruppe in UntPlanWeb geändert">geändert</span>' : ''}</td>
      <td>${e(u.lehrer.join(', '))}</td>
      <td><span class="ug-fach" style="--fach:${/^#[0-9a-f]{6}$/i.test(u.farbe) ? u.farbe : '#bfc8cf'}"><i></i><span class="nl-tag" title="${e(u.fachName)}">${e(u.fach)}</span></span></td>
      <td class="text-end">${u.wochenstunden}</td>
      <td><div class="ug-termine">${u.termine.map(t => `<span class="ug-termin">${e(t)}</span>`).join('') || '<span class="nl-unter">–</span>'}</div></td>
      <td>${zahlknopf(`data-schueler="${u.id}"`, 'person', u.schueler, 'Schüler*innen in der Schülergruppe')}</td>
      <td>${zahlknopf(`data-gruppen="${u.id}"`, 'people', u.gruppen, 'Schülergruppen des Unterrichts', u.gaeste > 0, u.gaeste ? ` <span class="nl-chip lila ms-1">${u.gaeste} Gast</span>` : '')}</td>
      <td class="text-end"><a class="nl-icon sichtbar" href="#/berichte/-/${encodeURIComponent(klasse)}/${von}/${bis}" title="Berichte" aria-label="Berichte"><i class="bi bi-file-earmark-bar-graph"></i></a></td></tr>`).join('')}
    </tbody></table>` : `<div class="p-3">${leer('Keine Unterrichte in diesem Zeitraum.', 'meineunterrichte')}</div>`;
  inhalt.querySelectorAll('[data-schueler]').forEach(b => b.onclick = () => mitgliederDialog(Number(b.dataset.schueler), 0, d.stichtag, neu));
  inhalt.querySelectorAll('[data-gruppen]').forEach(b => b.onclick = () => gruppenDialog(Number(b.dataset.gruppen), d.stichtag, neu));
  seite.querySelector('[data-suche]').oninput = ev => {
    const t = ev.target.value.trim().toLowerCase();
    inhalt.querySelectorAll('tbody tr').forEach(tr => { tr.hidden = t.length > 0 && !tr.dataset.name.includes(t); });
  };
  seite.querySelector('[data-wechsel]')?.addEventListener('click', () => unterrichtswechsel(el, klasse, neu));
  seite.querySelector('[data-bericht]').onclick = () => herunterladen(`unterricht-klasse-${klasse}-${von}-${bis}.csv`,
    ['U-Nr;Klassen;Schülergruppe;Lehrkraft;Fach;Std.;Termine;Schüler*innen;Schülergruppen',
      ...d.unterricht.map(u => [u.id, u.klassen.join(', '), u.gruppe, u.lehrer.join(', '), u.fach, u.wochenstunden, u.termine.join(', '), u.schueler, u.gruppen].map(csvFeld).join(';'))].join('\r\n'));
}

// ------------------------------------------------------------------ Schülergruppen eines Unterrichts

const zeitraum = (v, b) => v || b ? `${v ? datumDe(v) : '…'} – ${b ? datumDe(b) : '…'}` : 'ganzes Schuljahr';

export async function gruppenDialog(unterricht, stichtag, nachher) {
  const d = await holen(`/api/app/unterrichtklassen/${unterricht}/gruppen?datum=${stichtag}`);
  let geaendert = false;
  const box = dialog({
    titel: `Schülergruppen des Unterrichts ${d.fach} ${d.klassen.join(', ')}`, breit: true,
    inhalt: `<div class="ug-dialog">
      <div class="ug-kopf"><span>Lehrkraft: <b>${e(d.lehrer.join(', ') || '–')}</b></span><span>Fach: <b>${e(d.fach)}</b></span><span>Klasse: <b>${e(d.klassen.join(', '))}</b></span><span>Stand: <b>${datumDe(d.stichtag)}</b></span></div>
      <div class="nl-tabelle"><table><thead><tr><th>Name</th><th>Art</th><th>Zeitraum</th><th class="text-end">Schüler*innen</th><th></th></tr></thead><tbody>
      ${d.gruppen.map(g => `<tr>
        <td><span class="ug-name">${e(g.name)}</span></td>
        <td>${g.gast ? '<span class="nl-chip lila">Gast</span>' : '<span class="nl-chip grau">Standard</span>'}${!g.gast && g.abweichungen ? ` <span class="nl-chip gelb">${g.abweichungen} Abweichung${g.abweichungen === 1 ? '' : 'en'}</span>` : ''}</td>
        <td class="nl-unter text-nowrap">${e(zeitraum(g.von, g.bis))}${g.gast && !g.gilt ? ' <span class="nl-chip grau">nicht am Stichtag</span>' : ''}</td>
        <td class="text-end">${g.anzahl}</td>
        <td class="text-end text-nowrap">
          <button type="button" class="ug-zahlknopf" data-mitglieder="${g.id}" title="Schüler*innen in der Schülergruppe"><i class="bi bi-person"></i>Schüler*innen</button>
          ${d.schreiben && g.gast ? `<button type="button" class="nl-icon sichtbar" data-aendern="${g.id}" title="Bearbeiten" aria-label="Bearbeiten"><i class="bi bi-pencil"></i></button>` : ''}
          ${d.schreiben && (g.gast || (g.id && g.abweichungen)) ? `<button type="button" class="nl-icon sichtbar rot" data-loeschen="${g.id}" title="${g.gast ? 'Löschen' : 'Abweichungen zurücksetzen'}" aria-label="${g.gast ? 'Löschen' : 'Abweichungen zurücksetzen'}"><i class="bi bi-${g.gast ? 'trash' : 'arrow-counterclockwise'}"></i></button>` : ''}
        </td></tr>`).join('')}</tbody></table></div>
      <p class="ug-hinweis">Die Standardgruppe umfasst die Schüler*innen der Klassen; Abmeldungen und befristete Teilnahme werden je Schüler*in eingetragen.
        Gast-Schülergruppen „(Name)“ entstehen durch den befristeten Unterrichtswechsel und gelten genau für ihren Zeitraum.</p></div>`,
    knoepfe: [{ text: 'Schließen' }],
  });
  const modal = box.closest('.modal');
  modal.addEventListener('hidden.bs.modal', () => { if (geaendert) nachher?.(); });
  const schliessenUndNeu = () => { geaendert = true; box.schliessen(); };
  box.querySelectorAll('[data-mitglieder]').forEach(b => b.onclick = () => mitgliederDialog(unterricht, Number(b.dataset.mitglieder), stichtag, () => { geaendert = true; box.schliessen(); }));
  box.querySelectorAll('[data-aendern]').forEach(b => b.onclick = () => {
    const g = d.gruppen.find(x => x.id === Number(b.dataset.aendern));
    dialog({
      titel: `Schülergruppe ${g.name}`,
      inhalt: `<div class="ug-felder"><label>Name<input name="name" value="${e(g.name)}" maxlength="100"></label>
        <label>Von<input type="date" name="von" value="${g.von}"></label><label>Bis<input type="date" name="bis" value="${g.bis}"></label></div>`,
      knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async f => {
        const w = n => f.querySelector(`[name=${n}]`).value;
        await senden(`/api/app/unterrichtklassen/${unterricht}/gruppen/${g.id}`, { name: w('name'), von: w('von'), bis: w('bis') }, 'PUT');
        toast('Gespeichert.');
        schliessenUndNeu();
      } }],
    });
  });
  box.querySelectorAll('[data-loeschen]').forEach(b => b.onclick = async () => {
    const g = d.gruppen.find(x => x.id === Number(b.dataset.loeschen));
    const text = g.gast ? `Gast-Schülergruppe „${g.name}“ löschen? Die Gäste erscheinen dann nicht mehr im Klassenbuch dieses Unterrichts.`
      : `Alle Abweichungen der Schülergruppe „${g.name}“ zurücksetzen? Danach nehmen wieder alle Schüler*innen der Klassen teil.`;
    if (!await bestaetigen('Schülergruppe', text, g.gast ? 'Löschen' : 'Zurücksetzen')) return;
    try { await api(`/api/app/unterrichtklassen/${unterricht}/gruppen/${g.id}`, { methode: 'DELETE' }); toast(g.gast ? 'Gelöscht.' : 'Zurückgesetzt.'); schliessenUndNeu(); }
    catch (err) { toast(err.message, true); }
  });
}

// ------------------------------------------------------------------ Schüler*innen in der Schülergruppe

export async function mitgliederDialog(unterricht, gruppe, stichtag, nachher) {
  const d = await holen(`/api/app/unterrichtklassen/${unterricht}/gruppen/${gruppe}/schueler?datum=${stichtag}`);
  const zeilen = d.schueler.map(x => ({ ...x }));
  const status = x => !x.auswahl ? '<span class="nl-chip rot">abgemeldet</span>'
    : x.von || x.bis ? `<span class="nl-chip gelb">${e(zeitraum(x.von, x.bis))}</span>`
    : d.gruppe.gast ? `<span class="nl-chip lila">Gast</span>` : '<span class="nl-chip gruen">nimmt teil</span>';
  const zeile = (x, i) => `<tr data-i="${i}" class="${x.auswahl ? '' : 'ug-aus'}" data-name="${e(`${x.name} ${x.vorname} ${x.klasse}`.toLowerCase())}">
      <td class="nl-wahl"><input type="checkbox" class="nl-rund" data-auswahl ${x.auswahl ? 'checked' : ''} ${d.schreiben ? '' : 'disabled'} aria-label="${e(`${x.vorname} ${x.name} nimmt teil`)}"></td>
      <td class="nl-avatar-zelle"><span class="nl-avatar">${e(initialen(`${x.vorname} ${x.name}`))}</span></td>
      <td><div class="ug-name">${e(x.name)}, ${e(x.vorname)}</div><div class="nl-unter">${e(GESCHLECHT[x.geschlecht] ?? '')}</div></td>
      <td>${e(x.klasse)}</td>
      <td><input type="date" data-von value="${x.von}" ${d.schreiben ? '' : 'disabled'} aria-label="Von"></td>
      <td><input type="date" data-bis value="${x.bis}" ${d.schreiben ? '' : 'disabled'} aria-label="Bis (letzter Tag)"></td>
      <td data-status>${status(x)}</td></tr>`;
  const box = dialog({
    titel: `Schüler*innen in der Schülergruppe ${d.gruppe.name}`, breit: true,
    inhalt: `<div class="ug-dialog">
      <div class="ug-kopf"><span>Klasse: <b>${e(d.klassen.join(', '))}</b></span><span>Fach: <b>${e(d.fach)}</b></span><span>Schüler*innen: <b data-zahl>${zeilen.filter(x => x.auswahl).length}</b></span>
        ${d.gruppe.gast ? `<span>Zeitraum: <b>${e(zeitraum(d.gruppe.von, d.gruppe.bis))}</b></span>` : `<span>Stand: <b>${datumDe(d.stichtag)}</b></span>`}</div>
      <label class="nl-suche"><i class="bi bi-search" aria-hidden="true"></i><input type="search" data-suche placeholder="Name suchen" aria-label="Name suchen"></label>
      <div class="nl-tabelle">${zeilen.length ? `<table><thead><tr><th class="nl-wahl">${d.schreiben ? `<input type="checkbox" class="nl-rund" data-alle aria-label="Alle auswählen" ${zeilen.every(x => x.auswahl) ? 'checked' : ''}>` : ''}</th><th></th><th>Name</th><th>Klasse</th><th>Von</th><th>Bis</th><th>Teilnahme</th></tr></thead>
        <tbody>${zeilen.map(zeile).join('')}</tbody></table>` : leer('Keine Schüler*innen in dieser Gruppe.', 'people')}</div>
      <p class="ug-hinweis">${d.gruppe.gast ? 'Ohne Häkchen wird der Gast aus der Gruppe entfernt. Von/Bis schränken die Teilnahme innerhalb des Gruppenzeitraums ein.'
        : 'Ohne Häkchen ist die Schülerin bzw. der Schüler vom Unterricht abgemeldet. Mit „Bis“ (letzter Tag) endet die Teilnahme, mit „Von“ beginnt sie zu diesem Datum. Das Klassenbuch einer Stunde zeigt nur, wer an diesem Tag teilnimmt.'}</p></div>`,
    knoepfe: d.schreiben ? [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async () => {
      for (const x of zeilen) if (x.von && x.bis && x.von > x.bis) throw new Error(`${x.vorname} ${x.name}: „Von“ liegt nach „Bis“.`);
      await senden(`/api/app/unterrichtklassen/${unterricht}/gruppen/${gruppe}/schueler?datum=${stichtag}`,
        { schueler: zeilen.map(x => ({ schuelerId: x.id, auswahl: x.auswahl, von: x.von, bis: x.bis })) }, 'PUT');
      toast('Schülergruppe gespeichert.');
      nachher?.();
    } }] : [{ text: 'Schließen' }],
  });
  const zahl = () => { box.querySelector('[data-zahl]').textContent = zeilen.filter(x => x.auswahl).length; };
  box.querySelectorAll('tbody tr[data-i]').forEach(tr => {
    const x = zeilen[Number(tr.dataset.i)];
    const neuZeichnen = () => { tr.classList.toggle('ug-aus', !x.auswahl); tr.querySelector('[data-status]').innerHTML = status(x); zahl(); };
    tr.querySelector('[data-auswahl]').onchange = ev => { x.auswahl = ev.target.checked; neuZeichnen(); };
    tr.querySelector('[data-von]').onchange = ev => { x.von = ev.target.value; neuZeichnen(); };
    tr.querySelector('[data-bis]').onchange = ev => { x.bis = ev.target.value; neuZeichnen(); };
  });
  box.querySelector('[data-alle]')?.addEventListener('change', ev => {
    box.querySelectorAll('tbody tr[data-i]').forEach(tr => {
      if (tr.hidden) return;
      const c = tr.querySelector('[data-auswahl]');
      c.checked = ev.target.checked;
      c.dispatchEvent(new Event('change'));
    });
  });
  box.querySelector('[data-suche]').oninput = ev => {
    const t = ev.target.value.trim().toLowerCase();
    box.querySelectorAll('tbody tr[data-i]').forEach(tr => { tr.hidden = t.length > 0 && !tr.dataset.name.includes(t); });
  };
}

// ------------------------------------------------------------------ Befristeter Unterrichtswechsel (Assistent)

const SCHRITTE = ['Zeitraum', 'Schüler*innen', 'Zielklasse', 'Unterricht'];

export async function unterrichtswechsel(el, vorgabeKlasse, nachher) {
  const st = { schritt: 0, von: heuteIso(), bis: heuteIso(), filter: '', suche: '', schueler: new Set(), zielklasse: vorgabeKlasse, unterrichte: new Set(), liste: null, alle: null };
  const box = dialog({
    titel: 'Befristeter Unterrichtswechsel für Schüler*innen', breit: true, inhalt: '<div class="ug-dialog" data-schritt></div>',
    knoepfe: [
      { text: 'Abbrechen' },
      { text: 'Zurück', aktion: () => { if (st.schritt > 0) { st.schritt--; zeichnen(); } return false; } },
      { text: 'Weiter', art: 'haupt', aktion: async () => weiter() },
    ],
  });
  const knopfZurueck = box.querySelector('[data-knopf="1"]');
  const knopfWeiter = box.querySelector('[data-knopf="2"]');
  const inhalt = box.querySelector('[data-schritt]');

  async function weiter() {
    if (st.schritt === 0) {
      if (!st.von || !st.bis) throw new Error('Bitte den Zeitraum eingeben.');
      if (st.von > st.bis) throw new Error('„Bis“ liegt vor „Von“.');
    }
    if (st.schritt === 1 && !st.schueler.size) throw new Error('Bitte mindestens eine Schülerin bzw. einen Schüler wählen.');
    if (st.schritt === 2) {
      if (!st.zielklasse) throw new Error('Bitte die Zielklasse wählen.');
      st.liste = await holen(`/api/app/unterrichtswechsel/unterricht?klasse=${encodeURIComponent(st.zielklasse)}&von=${st.von}&bis=${st.bis}`);
      st.unterrichte = new Set([...st.unterrichte].filter(id => st.liste.some(u => u.id === id)));
    }
    if (st.schritt === 3) {
      if (!st.unterrichte.size) throw new Error('Bitte mindestens einen Unterricht wählen.');
      const r = await senden('/api/app/unterrichtswechsel', { von: st.von, bis: st.bis, schuelerIds: [...st.schueler], zielklasse: st.zielklasse, unterrichte: [...st.unterrichte] });
      toast(`Gespeichert: ${st.schueler.size} Schüler*innen in ${r.gruppen.length} Unterricht${r.gruppen.length === 1 ? '' : 'en'} der Klasse ${st.zielklasse}.`);
      nachher?.();
      return true;
    }
    st.schritt++;
    await zeichnen();
    return false;
  }

  async function zeichnen() {
    knopfZurueck.disabled = st.schritt === 0;
    knopfWeiter.textContent = st.schritt === SCHRITTE.length - 1 ? 'Speichern' : 'Weiter';
    const kopf = `<div class="ug-schritte">${SCHRITTE.map((t, i) => `<span class="ug-schritt ${i === st.schritt ? 'aktiv' : i < st.schritt ? 'fertig' : ''}"><b>${i + 1}</b>${t}</span>`).join('')}</div>`;
    if (st.schritt === 0) {
      inhalt.innerHTML = `${kopf}<div class="ug-frage">In welchem Zeitraum besuchen die Schüler*innen einen anderen Unterricht?</div>
        <div class="ug-felder"><label>Von<input type="date" data-von value="${st.von}"></label><label>Bis<input type="date" data-bis value="${st.bis}"></label></div>
        <p class="ug-hinweis">Zum Beispiel während einer Klassenfahrt für Schüler*innen, die nicht mitfahren.</p>`;
      inhalt.querySelector('[data-von]').onchange = ev => { st.von = ev.target.value; if (st.bis < st.von) { st.bis = st.von; inhalt.querySelector('[data-bis]').value = st.bis; } };
      inhalt.querySelector('[data-bis]').onchange = ev => { st.bis = ev.target.value; };
    } else if (st.schritt === 1) {
      if (!st.alle) st.alle = (await holen('/api/app/schueler')).schueler;
      const liste = () => st.alle.filter(x => (!st.filter || x.klasse === st.filter) && (!st.suche || `${x.name} ${x.vorname}`.toLowerCase().includes(st.suche)));
      inhalt.innerHTML = `${kopf}<div class="ug-frage">Bitte wählen Sie die Schüler*innen</div>
        <div class="ug-felder mb-3"><label>Klasse<select data-filter><option value="">- Alle -</option>${el.klassen.map(k => `<option ${k.kurz === st.filter ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select></label>
          <label class="flex-grow-1">Suche<input type="search" data-suche value="${e(st.suche)}" placeholder="Name"></label></div>
        <div class="d-flex flex-wrap gap-2 align-items-center mb-2"><span class="nl-zahl"><b data-anzahl>${st.schueler.size}</b> gewählt</span>
          <button type="button" class="nl-knopf" data-alle>Alle angezeigten wählen</button><button type="button" class="nl-knopf" data-keine>Auswahl leeren</button></div>
        <div class="ug-liste" data-liste></div>`;
      const listeEl = inhalt.querySelector('[data-liste]');
      const listeZeichnen = () => {
        const l = liste();
        listeEl.innerHTML = l.length ? l.map(x => `<label class="${st.schueler.has(x.id) ? 'gewaehlt' : ''}"><input type="checkbox" class="nl-rund" value="${x.id}" ${st.schueler.has(x.id) ? 'checked' : ''}>
          <span>${e(x.name)} ${e(x.vorname)}</span><small>${e(x.klasse)}</small></label>`).join('') : `<div class="p-3">${leer('Keine Schüler*innen gefunden.', 'people')}</div>`;
        listeEl.querySelectorAll('input').forEach(c => c.onchange = () => {
          const id = Number(c.value);
          if (c.checked) st.schueler.add(id); else st.schueler.delete(id);
          c.closest('label').classList.toggle('gewaehlt', c.checked);
          inhalt.querySelector('[data-anzahl]').textContent = st.schueler.size;
        });
      };
      listeZeichnen();
      inhalt.querySelector('[data-filter]').onchange = ev => { st.filter = ev.target.value; listeZeichnen(); };
      inhalt.querySelector('[data-suche]').oninput = ev => { st.suche = ev.target.value.trim().toLowerCase(); listeZeichnen(); };
      inhalt.querySelector('[data-alle]').onclick = () => { liste().forEach(x => st.schueler.add(x.id)); listeZeichnen(); inhalt.querySelector('[data-anzahl]').textContent = st.schueler.size; };
      inhalt.querySelector('[data-keine]').onclick = () => { st.schueler.clear(); listeZeichnen(); inhalt.querySelector('[data-anzahl]').textContent = 0; };
    } else if (st.schritt === 2) {
      inhalt.innerHTML = `${kopf}<div class="ug-frage">Bitte wählen Sie die Zielklasse</div>
        <div class="ug-felder"><label>Klasse<select data-ziel><option value="">Klasse wählen</option>${el.klassen.map(k => `<option ${k.kurz === st.zielklasse ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select></label></div>
        <p class="ug-hinweis">${st.schueler.size} Schüler*innen besuchen vom ${datumDe(st.von)} bis ${datumDe(st.bis)} den Unterricht dieser Klasse.</p>`;
      inhalt.querySelector('[data-ziel]').onchange = ev => { st.zielklasse = ev.target.value; };
    } else {
      inhalt.innerHTML = `${kopf}<div class="ug-frage">Bitte wählen Sie den Unterricht der Klasse ${e(st.zielklasse)}</div>
        <div class="nl-tabelle">${st.liste.length ? `<table><thead><tr><th class="nl-wahl"><input type="checkbox" class="nl-rund" data-alle aria-label="Alle auswählen"></th><th>Klasse</th><th>Lehrkraft</th><th>Fach</th><th class="text-end">Std.</th><th>Schülergruppen</th></tr></thead><tbody>
          ${st.liste.map(u => `<tr><td class="nl-wahl"><input type="checkbox" class="nl-rund" value="${u.id}" ${st.unterrichte.has(u.id) ? 'checked' : ''} aria-label="${e(`${u.fach} ${u.lehrer.join(', ')}`)}"></td>
            <td>${e(u.klassen.join(', '))}</td><td>${e(u.lehrer.join(', '))}</td><td><span class="nl-tag">${e(u.fach)}</span></td><td class="text-end">${u.wochenstunden}</td>
            <td>${u.gruppen ? `<span class="nl-chip grau"><i class="bi bi-people"></i>${u.gruppen}</span>` : ''}</td></tr>`).join('')}</tbody></table>` : leer('Die Klasse hat in diesem Zeitraum keinen Unterricht.', 'meineunterrichte')}</div>
        <p class="ug-hinweis">Je Unterricht entsteht eine Gast-Schülergruppe „(Name)“ genau für den Zeitraum. Die Schüler*innen erscheinen dann im Klassenbuch dieser Stunden;
          nachträglich lässt sich die Gruppe unter „Schülergruppen“ bearbeiten.</p>`;
      const boxen = [...inhalt.querySelectorAll('tbody input')];
      boxen.forEach(c => c.onchange = () => { const id = Number(c.value); if (c.checked) st.unterrichte.add(id); else st.unterrichte.delete(id); });
      inhalt.querySelector('[data-alle]')?.addEventListener('change', ev => boxen.forEach(c => { c.checked = ev.target.checked; c.dispatchEvent(new Event('change')); }));
    }
  }
  try { await zeichnen(); } catch (err) { toast(err.message, true); }
}

// ------------------------------------------------------------------ Klassenwechsel (Schülerverwaltung)

export async function klassenwechselDialog(schueler, klassen, nachher) {
  let d = await holen(`/api/app/schueler/${schueler.id}/klassenwechsel`);
  const heute = heuteIso();
  const verlauf = () => `<table class="ug-verlauf"><thead><tr><th>Klasse</th><th>Beginndatum</th><th>Enddatum</th><th></th></tr></thead><tbody>
    ${d.verlauf.map(z => `<tr class="${(!z.beginn || z.beginn <= heute) && (!z.ende || heute <= z.ende) ? 'aktuell' : ''}"><td class="ug-name">${e(z.klasse)}</td><td>${datumDe(z.beginn) || '–'}</td><td>${datumDe(z.ende) || '–'}</td>
      <td class="text-end">${d.bearbeiten && z.wechselId ? `<button type="button" class="nl-icon sichtbar rot" data-weg="${z.wechselId}" title="Klassenwechsel löschen" aria-label="Klassenwechsel löschen"><i class="bi bi-trash"></i></button>` : ''}</td></tr>`).join('')}</tbody></table>`;
  let geaendert = false;
  const box = dialog({
    titel: `Klassenwechsel für ${schueler.name} ${schueler.vorname}`,
    inhalt: `<div class="ug-dialog">${d.bearbeiten ? `<div class="ug-felder"><label>Neue Klasse<select name="klasse">${klassen.filter(k => k.kurz !== schueler.klasse).map(k => `<option>${e(k.kurz)}</option>`).join('')}</select></label>
        <label>Beginndatum<input type="date" name="beginn" value="${heute}"></label></div>` : ''}
      <div data-verlauf>${verlauf()}</div>
      <p class="ug-hinweis">Die Klassenzugehörigkeit gilt ab dem Beginndatum: Klassenbuch, Schülerlisten und Fehlzeiten berücksichtigen das Datum. Die Daten aus dem Desktop-Programm bleiben unverändert.</p></div>`,
    knoepfe: d.bearbeiten ? [{ text: 'Abbrechen' }, { text: 'Weiter', art: 'haupt', aktion: async f => {
      d = await senden(`/api/app/schueler/${schueler.id}/klassenwechsel`, { klasse: f.querySelector('[name=klasse]').value, beginn: f.querySelector('[name=beginn]').value });
      toast('Klassenwechsel gespeichert.');
      geaendert = true;
    } }] : [{ text: 'Schließen' }],
  });
  box.closest('.modal').addEventListener('hidden.bs.modal', () => { if (geaendert) nachher?.(); });
  const binden = () => box.querySelectorAll('[data-weg]').forEach(b => b.onclick = async () => {
    if (!await bestaetigen('Klassenwechsel löschen', 'Diesen Klassenwechsel löschen?')) return;
    try {
      d = await api(`/api/app/schueler/${schueler.id}/klassenwechsel/${b.dataset.weg}`, { methode: 'DELETE' });
      geaendert = true;
      box.querySelector('[data-verlauf]').innerHTML = verlauf();
      binden();
    } catch (err) { toast(err.message, true); }
  });
  binden();
}
