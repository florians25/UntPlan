// Unterricht → Mein Unterricht im neuen Listendesign: Suche, Filterpillen (Aktivitätsart, Zeitraum), Tabelle mit Klasse,
// Schülergruppe, Fach, Lehrkraft, Wochenstunden und Schnellzugriffen auf Klassenbuch, Termine, Lehrstoff, Fehlzeiten, Schülergruppe und Berichte.
// Unterricht → Klasse zeigt dieselbe Liste für eine gewählte Klasse.
import { e, holen, leer, herunterladen, heuteIso, plusTage, montag } from '../ui.js';
import { elementWahl } from '../elementwahl.js';

const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';
const BEREICHE = {
  woche: ['Aktuelle Woche', () => [montag(heuteIso()), plusTage(montag(heuteIso()), 6)]],
  naechste: ['Nächste Woche', () => [plusTage(montag(heuteIso()), 7), plusTage(montag(heuteIso()), 13)]],
  monat: ['Aktueller Monat', () => { const h = heuteIso(); const d = new Date(h.slice(0, 7) + '-01T12:00'); d.setMonth(d.getMonth() + 1); d.setDate(0); return [h.slice(0, 8) + '01', h.slice(0, 8) + String(d.getDate()).padStart(2, '0')]; }],
  jahr: ['Schuljahr', null],
};

export async function zeigen(seite, kontext, [artParam, elementParam, bereichParam, vonParam, bisParam, aktParam]) {
  const ich = kontext.ich;
  const el = await kontext.elemente();
  const sj = kontext.schuljahr;
  const art = artParam === 'klasse' ? 'klasse' : 'lehrer';
  const element = (elementParam && elementParam !== '-' ? elementParam : '') || (art === 'lehrer' ? ich.element : el.klassen[0]?.kurz) || '';
  const iso = /^\d{4}-\d{2}-\d{2}$/;
  const bereich = BEREICHE[bereichParam] ? bereichParam : 'woche';
  let [von, bis] = bereich === 'jahr' ? [sj?.von ?? heuteIso(), sj?.bis ?? heuteIso()] : BEREICHE[bereich][1]();
  if (iso.test(vonParam ?? '')) von = vonParam;
  if (iso.test(bisParam ?? '')) bis = bisParam;
  const aktivitaet = aktParam && aktParam !== '-' ? aktParam : '';
  const ziel = (a = art, x = element, b = bereich, v = von, bi = bis, ak = aktivitaet) => `#/unterricht/${a}/${encodeURIComponent(x || '-')}/${b}/${v}/${bi}/${encodeURIComponent(ak || '-')}`;
  const titel = art === 'lehrer' ? `${element === ich.element ? 'Mein Unterricht' : 'Unterricht'} ${element}` : `Unterricht ${element}`;
  // Unterricht | Mein Unterricht | Neuer Unterricht (Buchungsmodul): Unterricht anlegen und zusätzliche Stunden verplanen
  kontext.titel(titel, ich.kollegium ? '<a class="nl-knopf" href="#/buchen/unterricht"><i class="bi bi-plus-lg"></i>Neuer Unterricht</a>' : '');
  const pille = (inhalt, aktiv = false) => `<label class="nl-pille ${aktiv ? 'aktiv' : ''}">${inhalt}</label>`;
  seite.innerHTML = `
    <div class="nl-leiste">
      <label class="nl-suche"><i class="bi bi-search" aria-hidden="true"></i><input type="search" data-suche placeholder="Suchen" aria-label="Unterricht suchen"></label>
      ${art === 'klasse' ? '<span data-wahl></span>' : ''}
      ${pille(`<select data-akt aria-label="Aktivitätsart"><option value="">Aktivitätsart</option>${['Unterricht', 'Vertretung', 'Prüfung'].map(a => `<option ${a === aktivitaet ? 'selected' : ''}>${a}</option>`).join('')}</select>`, !!aktivitaet)}
      ${pille(`<span class="me-1 text-body-secondary">Von</span><input type="date" data-von value="${von}">`)}
      ${pille(`<span class="me-1 text-body-secondary">Bis</span><input type="date" data-bis value="${bis}">`)}
      ${pille(`<select data-bereich aria-label="Zeitraum">${Object.entries(BEREICHE).map(([k, [t]]) => `<option value="${k}" ${k === bereich && !vonParam ? 'selected' : ''}>${t}</option>`).join('')}${vonParam ? '<option selected>Benutzerdefiniert</option>' : ''}</select>`)}
      <span class="ms-auto d-flex gap-2"><button type="button" class="nl-knopf" data-bericht><i class="bi bi-filetype-csv"></i>Bericht</button></span>
      <button type="button" class="d-none" data-filtern></button>
    </div>
    <div class="nl-tabelle" data-inhalt></div>`;
  const wahl = seite.querySelector('[data-wahl]');
  if (wahl) wahl.replaceWith(elementWahl({ eintraege: el.klassen, wert: element, platzhalter: 'Klasse wählen', beiWahl: x => kontext.navigieren(ziel(art, x)) }));
  const f = n => seite.querySelector(`[data-${n}]`).value;
  seite.querySelector('[data-bereich]').onchange = ev => BEREICHE[ev.target.value] && kontext.navigieren(`#/unterricht/${art}/${encodeURIComponent(element || '-')}/${ev.target.value}`);
  seite.querySelector('[data-filtern]').onclick = () => kontext.navigieren(ziel(art, element, bereich, f('von'), f('bis'), f('akt')));
  seite.querySelectorAll('[data-von], [data-bis]').forEach(x => x.onchange = () => kontext.navigieren(ziel(art, element, bereich, f('von'), f('bis'), f('akt'))));
  seite.querySelector('[data-akt]').onchange = () => kontext.navigieren(ziel(art, element, bereich, f('von'), f('bis'), f('akt')));
  const inhalt = seite.querySelector('[data-inhalt]');
  if (!element) { inhalt.innerHTML = `<div class="p-3">${leer('Bitte eine Klasse wählen.', 'meineunterrichte')}</div>`; return; }

  // Unterrichte der Periode und die Stunden im Zeitraum (geänderte Unterrichte lila)
  const [liste, stunden] = await Promise.all([
    holen(`/api/app/unterricht?${art}=${encodeURIComponent(element)}&datum=${von}`),
    art === 'lehrer' ? holen(`/api/app/klassenbuch/stunden?von=${von}&bis=${bis}&lehrer=${encodeURIComponent(element)}`).catch(() => null) : Promise.resolve(null),
  ]);
  const geaendert = new Set((stunden?.zeilen ?? []).filter(z => z.status !== 'regulaer').map(z => z.unterricht));
  const zeilen = liste.map(u => ({ ...u, aktivitaet: geaendert.has(u.id) ? 'Vertretung' : 'Unterricht' })).filter(u => !aktivitaet || u.aktivitaet === aktivitaet || (aktivitaet === 'Prüfung' && /klausur|prüfung/i.test(u.text)));
  const knopf = (href, icon, titel) => `<a class="nl-icon sichtbar" href="${href}" title="${titel}" aria-label="${titel}"><i class="bi bi-${icon}"></i></a>`;
  inhalt.innerHTML = zeilen.length ? `<table><thead><tr><th>Klasse</th><th>Schülergruppe</th><th>Fach</th><th>Lehrkraft</th><th class="text-end">Wst</th><th>Zeitraum</th><th>Aktivität</th><th></th></tr></thead><tbody>
    ${zeilen.map(u => { const k = u.klassen[0] ?? ''; return `<tr data-name="${e([u.klassen.join(' '), u.kurs, u.fach, u.lehrer.join(' ')].join(' ').toLowerCase())}">
      <td><div class="nl-name">${e(u.klassen.join(', ') || '–')}</div></td>
      <td class="nl-unter">${e(u.kurs || (k ? `SG_${u.klassen.join('_')}` : ''))}</td>
      <td><span class="nl-tag">${e(u.fach)}</span></td><td>${e(u.lehrer.join(', '))}</td>
      <td class="text-end">${u.wochenstunden}</td>
      <td class="text-nowrap nl-unter">${datumDe(sj?.von)} – ${datumDe(sj?.bis)}</td>
      <td>${u.aktivitaet === 'Vertretung' ? '<span class="nl-chip lila">Vertretung</span>' : '<span class="nl-chip grau">Unterricht</span>'}</td>
      <td class="text-end text-nowrap nl-aktionen">
        ${knopf(`#/klassenbuch/${encodeURIComponent(k || '-')}/woche`, 'journal-text', 'Klassenbuch')}
        ${knopf(`#/plan/klasse/${encodeURIComponent(k)}/${von}`, 'calendar3-week', e(u.termine.join(', ') || 'Termine im Stundenplan'))}
        ${knopf(`#/klassenbuch/${encodeURIComponent(k)}/verlauf`, 'card-text', 'Lehrstoff')}
        ${knopf(`#/klassenbuch/${encodeURIComponent(k)}/fehlzeiten`, 'person-x', 'Schüler*innen und Fehlzeiten')}
        ${knopf(`#/schueler/${encodeURIComponent(k)}`, 'people', 'Schülergruppe')}
        ${knopf(`#/berichte/-/${encodeURIComponent(k)}/${von}/${bis}`, 'file-earmark-bar-graph', 'Berichte')}</td></tr>`; }).join('')}
    </tbody></table>` : `<div class="p-3">${leer('Keine Unterrichte gefunden.', 'meineunterrichte')}</div>`;
  seite.querySelector('[data-suche]').oninput = ev => {
    const t = ev.target.value.trim().toLowerCase();
    inhalt.querySelectorAll('tbody tr').forEach(tr => { tr.hidden = t.length > 0 && !tr.dataset.name.includes(t); });
  };
  seite.querySelector('[data-bericht]').onclick = () => herunterladen(`unterricht-${element}-${von}-${bis}.csv`,
    ['Aktivitätsart;Klassen;Schülergruppe;Fach;Lehrkraft;Wochenstunden;Termine;Räume', ...zeilen.map(u => [u.aktivitaet, u.klassen.join(', '), u.kurs, u.fach, u.lehrer.join(', '), u.wochenstunden, u.termine.join(', '), u.raeume.join(', ')]
      .map(x => `"${String(x ?? '').replace(/"/g, '""')}"`).join(';'))].join('\r\n'));
}
