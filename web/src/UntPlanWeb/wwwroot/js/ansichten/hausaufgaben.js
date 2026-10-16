// Hausaufgaben: nach Fälligkeit geordnet, Filter nach Zeitraum, Klasse und Lehrkraft; Klassenkonten zusätzlich mit Reiter „Lehrstoff“.
import { e, icon, holen, leer, filterFeld, datumKachel, datumKurz, heuteIso, plusTage, montag } from '../ui.js';

export async function zeigen(seite, kontext, [reiterParam, vonParam, bisParam, klasseParam, lehrerParam]) {
  // Schüler*innen und Erziehungsberechtigte: Hausaufgaben des Kindes (Datum + Woche/Monat)
  if (kontext.familie) return (await import('./familie.js')).hausaufgaben(seite, kontext, [reiterParam, vonParam]);
  const ich = kontext.ich;
  if (ich.kollegium && !/^\d{4}-/.test(vonParam ?? '')) return hausaufgabenListe(seite, kontext, reiterParam);
  const el = await kontext.elemente();
  const iso = /^\d{4}-\d{2}-\d{2}$/;
  const reiter = reiterParam === 'lehrstoff' ? 'lehrstoff' : 'offen';
  const von = iso.test(vonParam ?? '') ? vonParam : plusTage(heuteIso(), -7);
  const bis = iso.test(bisParam ?? '') ? bisParam : plusTage(heuteIso(), 28);
  const klasse = ich.kollegium ? (klasseParam && klasseParam !== '-' ? klasseParam : '') : ich.element;
  const lehrer = ich.kollegium ? (lehrerParam && lehrerParam !== '-' ? lehrerParam : (!klasseParam && ich.element ? ich.element : '')) : '';
  kontext.titel('Hausaufgaben', `<button class="btn btn-outline-secondary" data-drucken>${icon('drucken')}Drucken</button>`);
  const ziel = (r, v, b, k, l) => `#/hausaufgaben/${r}/${v}/${b}/${encodeURIComponent(k || '-')}/${encodeURIComponent(l || '-')}`;
  seite.innerHTML = `
    ${!ich.kollegium ? `<nav class="u-reiter mb-3 nicht-drucken"><a href="${ziel('offen', von, bis)}" class="${reiter === 'offen' ? 'aktiv' : ''}">Hausaufgaben</a><a href="${ziel('lehrstoff', von, bis)}" class="${reiter === 'lehrstoff' ? 'aktiv' : ''}">Lehrstoff</a></nav>` : ''}
    <div class="u-filter nicht-drucken">
      ${filterFeld('Von', `<input type="date" class="form-control" data-von value="${von}">`)}
      ${filterFeld('Bis', `<input type="date" class="form-control" data-bis value="${bis}">`)}
      ${ich.kollegium ? filterFeld('Klasse', `<select class="form-select" data-klasse><option value="">alle</option>${el.klassen.map(k => `<option ${k.kurz === klasse ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select>`)
        + filterFeld('Lehrkraft', `<select class="form-select" data-lehrer><option value="">alle</option>${el.lehrer.map(l => `<option value="${e(l.kurz)}" ${l.kurz === lehrer ? 'selected' : ''}>${e(l.kurz)}</option>`).join('')}</select>`) : ''}
    </div>
    <div class="card" data-inhalt></div>`;
  const f = n => seite.querySelector(`[data-${n}]`)?.value ?? '';
  seite.querySelectorAll('.u-filter input, .u-filter select').forEach(x => x.onchange = () => kontext.navigieren(ziel(reiter, f('von') || von, f('bis') || bis, f('klasse'), f('lehrer'))));
  document.querySelector('[data-drucken]').onclick = () => window.print();
  const inhalt = seite.querySelector('[data-inhalt]');

  if (reiter === 'lehrstoff') {
    const liste = await holen(`/api/app/klassenbuch/klasse?klasse=${encodeURIComponent(klasse ?? '')}&von=${von}&bis=${bis}`);
    const mit = liste.filter(x => x.thema);
    inhalt.innerHTML = mit.length ? `<ul class="u-liste">${mit.map(x => `<li>${datumKachel(x.datum)}<div class="u-zeilentext"><div class="titel">${e(x.fach)} <span class="fw-normal text-body-secondary">· ${x.stunde}. Std. · ${e(x.lehrer)}</span></div><div>${e(x.thema)}</div>
        ${x.notizSchueler ? `<div class="small text-body-secondary mt-1">${icon('info')} ${e(x.notizSchueler)}</div>` : ''}</div></li>`).join('')}</ul>` : leer('Kein Lehrstoff im Zeitraum.', 'eintraege');
    return;
  }

  const q = `von=${von}&bis=${bis}${klasse ? `&klasse=${encodeURIComponent(klasse)}` : ''}${lehrer ? `&lehrer=${encodeURIComponent(lehrer)}` : ''}`;
  const liste = await holen(`/api/app/hausaufgaben?${q}`);
  const heute = heuteIso();
  const gruppen = new Map();
  for (const h of liste) { const s = h.faellig ?? h.datum; gruppen.set(s, [...(gruppen.get(s) ?? []), h]); }
  inhalt.innerHTML = liste.length ? `<div class="card-header">${liste.length} ${liste.length === 1 ? 'Hausaufgabe' : 'Hausaufgaben'}</div>
    <ul class="u-liste">${[...gruppen.entries()].map(([datum, hs]) => `<li class="${datum < heute ? 'opacity-75' : ''}">${datumKachel(datum)}
      <div class="u-zeilentext d-flex flex-column gap-2">${hs.map(h => `<div><div class="titel">${e(h.fach)} <span class="fw-normal text-body-secondary">· ${e(h.klassen.join(', '))} · ${e(h.lehrer)}</span></div>
        <div>${e(h.hausaufgabe)}</div><div class="unter">erteilt am ${datumKurz(h.datum)} (${h.stunde}. Std.)${h.faellig ? '' : ' · ohne Fälligkeit'}</div></div>`).join('')}</div>
      <span class="u-chip ${datum < heute ? '' : datum <= plusTage(heute, 1) ? 'orange' : ''} align-self-start">${datum < heute ? 'fällig gewesen' : 'fällig'}</span></li>`).join('')}</ul>`
    : leer('Keine Hausaufgaben im Zeitraum.', 'hausaufgaben');
}

// ------------------------------------------------------------------ Liste wie im Original (UW-08): Klassenbuch → Hausaufgaben

const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';
const BEREICHE = {
  woche: ['Aktuelle Woche', () => [montag(heuteIso()), plusTage(montag(heuteIso()), 6)]],
  vier: ['Nächste 4 Wochen', () => [heuteIso(), plusTage(heuteIso(), 27)]],
  monat: ['Letzte 4 Wochen', () => [plusTage(heuteIso(), -27), heuteIso()]],
  jahr: ['Schuljahr', null],
};

async function hausaufgabenListe(seite, kontext, bereichParam) {
  const ich = kontext.ich;
  const sj = kontext.schuljahr;
  const bereich = BEREICHE[bereichParam] ? bereichParam : 'woche';
  const [von, bis] = bereich === 'jahr' ? [sj?.von ?? heuteIso(), sj?.bis ?? heuteIso()] : BEREICHE[bereich][1]();
  const basis = location.hash.startsWith('#/aufgaben') ? '#/aufgaben' : '#/hausaufgaben';
  const w = kontext.werkzeug(`<div class="u-suche">${icon('suchen')}<input type="search" data-suche placeholder="Suche" aria-label="Suche">
      <span class="u-trenn"></span><select data-wahl aria-label="Auswahl"><option value="meine">${ich.element ? 'Meine' : 'Alle'}</option>${ich.element ? '<option value="alle">Alle</option>' : ''}</select></div>
    <div class="u-zeitraum"><span>${datumDe(von)} - ${datumDe(bis)}</span>
      <span class="u-pillenwahl"><select data-bereich aria-label="Zeitraum">${Object.entries(BEREICHE).map(([k, [t]]) => `<option value="${k}" ${k === bereich ? 'selected' : ''}>${t}</option>`).join('')}</select></span></div>`);
  w.querySelector('[data-bereich]').onchange = ev => kontext.navigieren(`${basis}/${ev.target.value}`);
  let wahl = ich.element ? 'meine' : 'alle';
  let filter = '';
  let daten = [];
  const laden = async () => {
    daten = await holen(`/api/app/hausaufgaben?von=${von}&bis=${bis}${wahl === 'meine' && ich.element ? `&lehrer=${encodeURIComponent(ich.element)}` : ''}`);
    zeichnen();
  };
  const zeichnen = () => {
    const l = daten.filter(h => !filter || `${h.fach} ${h.lehrer} ${h.hausaufgabe} ${h.klassen.join(' ')}`.toLowerCase().includes(filter));
    kontext.titel(basis === '#/aufgaben' ? 'Aufgaben' : 'Hausaufgaben', '', String(l.length));
    seite.innerHTML = `<div class="u-tabelle-karte">${l.length ? `<table class="u-tabelle"><thead><tr><th>Aufgabendatum</th><th>Fälligkeitsdatum</th><th>Fach</th><th>Klasse</th><th>Erstellt von</th><th>Hausaufgabe</th><th>Anhänge</th></tr></thead><tbody>
      ${l.map(h => `<tr><td>${datumDe(h.datum)}</td><td>${datumDe(h.faellig)}</td><td>${e(h.fach)}</td><td>${e(h.klassen.join(', '))}</td><td>${e(h.lehrer)}</td>
        <td class="ha-text" title="${e(h.hausaufgabe)}">${e(h.hausaufgabe.length > 40 ? h.hausaufgabe.slice(0, 18) + ' …' : h.hausaufgabe)}</td><td></td></tr>`).join('')}</tbody></table>`
      : leer('Keine Hausaufgaben im Zeitraum.', 'hausaufgaben')}</div>`;
  };
  w.querySelector('[data-wahl]').onchange = ev => { wahl = ev.target.value; laden(); };
  w.querySelector('[data-suche]').oninput = ev => { filter = ev.target.value.trim().toLowerCase(); zeichnen(); };
  await laden();
}
