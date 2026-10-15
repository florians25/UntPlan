// Unterricht → Prüfungen (UW-08) wie im Original: oben Suche mit Auswahl „Alle / Meine“, rechts Zeitraum und „Aktuelle Woche“;
// darunter „Prüfungen 48“ mit „Berichte“ und die Liste (Auswahlkreis, Art, Name, Klasse, Fach, Lehrkraft, Datum, Von, Bis, Räume,
// Aufgegeben am, Rückgabe am, Text). Klausuren aus UntPlan und in UntPlanWeb vorgemerkte Prüfungen.
import { e, icon, holen, leer, heuteIso, plusTage, montag, herunterladen } from '../ui.js';

const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';
const BEREICHE = {
  woche: ['Aktuelle Woche', () => [montag(heuteIso()), plusTage(montag(heuteIso()), 6)]],
  naechste: ['Nächste Woche', () => [plusTage(montag(heuteIso()), 7), plusTage(montag(heuteIso()), 13)]],
  monat: ['Nächste 4 Wochen', () => [heuteIso(), plusTage(heuteIso(), 27)]],
  jahr: ['Schuljahr', null],
};

export async function zeigen(seite, kontext, [bereichParam, wahlParam]) {
  const ich = kontext.ich;
  const sj = kontext.schuljahr;
  const bereich = BEREICHE[bereichParam] ? bereichParam : 'woche';
  const [von, bis] = bereich === 'jahr' ? [sj?.von ?? heuteIso(), sj?.bis ?? plusTage(heuteIso(), 300)] : BEREICHE[bereich][1]();
  const wahl = ich.kollegium && ich.element ? (wahlParam === 'meine' ? 'meine' : 'alle') : 'alle';
  const ziel = (b = bereich, w = wahl) => `#/pruefungen/${b}/${w}`;
  const w = kontext.werkzeug(`<div class="u-suche">${icon('suchen')}<input type="search" data-suche placeholder="Suche" aria-label="Suche">
      <span class="u-trenn"></span><select data-wahl aria-label="Auswahl"><option value="alle">Alle</option>${ich.kollegium && ich.element ? `<option value="meine" ${wahl === 'meine' ? 'selected' : ''}>Meine</option>` : ''}</select></div>
    <div class="u-zeitraum"><span>${datumDe(von)} - ${datumDe(bis)}</span>
      <span class="u-pillenwahl"><select data-bereich aria-label="Zeitraum">${Object.entries(BEREICHE).map(([k, [t]]) => `<option value="${k}" ${k === bereich ? 'selected' : ''}>${t}</option>`).join('')}</select></span></div>`);
  w.querySelector('[data-bereich]').onchange = ev => kontext.navigieren(ziel(ev.target.value));
  w.querySelector('[data-wahl]').onchange = ev => kontext.navigieren(ziel(bereich, ev.target.value));
  const liste = await holen(`/api/app/pruefungen?von=${von}&bis=${bis}${wahl === 'meine' ? `&lehrer=${encodeURIComponent(ich.element)}` : ''}`);
  const auswahl = new Set();
  let filter = '';
  const zeichnen = () => {
    const l = liste.filter(p => !filter || [p.art, p.titel, p.fach, p.text, ...p.klassen, ...p.lehrer, ...p.raeume].join(' ').toLowerCase().includes(filter));
    kontext.titel('Prüfungen', `${ich.kollegium ? `<a class="btn btn-outline-secondary" href="#/melden/klausur">${icon('plus')}Prüfung eintragen</a>` : ''}<button class="btn btn-outline-secondary" data-berichte>Berichte</button>`, String(l.length));
    seite.innerHTML = `<div class="u-tabelle-karte">${l.length ? `<table class="u-tabelle pr-liste"><thead><tr><th></th><th>Art</th><th>Name</th><th>Klasse</th><th>Fach</th><th>Lehrkraft</th><th>Datum</th><th>Von</th><th>Bis</th><th>Räume</th><th>Aufgegeben am</th><th>Rückgabe am</th><th>Text</th></tr></thead><tbody>
      ${l.map(p => { const i = liste.indexOf(p); return `<tr class="${p.datum < heuteIso() ? 'vergangen' : ''}">
        <td><button type="button" class="u-auswahl ${auswahl.has(i) ? 'an' : ''}" data-wahl-i="${i}" aria-label="Prüfung auswählen"></button></td>
        <td>${e(p.art)}</td><td>${e(p.titel)}</td><td>${e(p.klassen.join(', '))}</td><td>${e(p.fach)}</td><td>${e(p.lehrer.join(', '))}</td>
        <td>${datumDe(p.datum)}</td><td>${e(p.beginn)}</td><td>${e(p.ende)}</td><td>${e(p.raeume.join(', '))}</td><td>${datumDe(p.aufgegeben)}</td><td></td>
        <td class="pr-text">${p.text ? `<span class="u-statuspille klein" title="${e(p.text)}">${e(p.text.length > 22 ? p.text.slice(0, 21) + '…' : p.text)}</span>` : ''}${p.status === 'vorgemerkt' ? ' <span class="u-chip">vorgemerkt</span>' : ''}</td></tr>`; }).join('')}
      </tbody></table>` : leer('Keine Prüfungen im gewählten Zeitraum.', 'pruefung')}</div>`;
    seite.querySelectorAll('[data-wahl-i]').forEach(b => b.onclick = () => { const i = Number(b.dataset.wahlI); if (auswahl.has(i)) auswahl.delete(i); else auswahl.add(i); zeichnen(); });
    document.querySelector('[data-berichte]').onclick = () => {
      const zeilen = auswahl.size ? [...auswahl].map(i => liste[i]) : l;
      herunterladen(`pruefungen-${von}-${bis}.csv`, ['Art;Name;Klassen;Fach;Lehrkräfte;Datum;Von;Bis;Räume;Aufgegeben am;Text;Status',
        ...zeilen.map(p => [p.art, p.titel, p.klassen.join(', '), p.fach, p.lehrer.join(', '), datumDe(p.datum), p.beginn, p.ende, p.raeume.join(', '), datumDe(p.aufgegeben), p.text, p.status]
          .map(x => `"${String(x ?? '').replace(/"/g, '""')}"`).join(';'))].join('\r\n'));
    };
  };
  zeichnen();
  w.querySelector('[data-suche]').oninput = ev => { filter = ev.target.value.trim().toLowerCase(); zeichnen(); };
}
