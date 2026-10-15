// Unterricht → Mein Unterricht (UW-07) im alten Listenmodus wie im Original: „Mein Unterricht ASL“, Filter Aktivitätsart und
// Zeitraum (mit „Aktuelle Woche“ …), grau gestreifte Liste mit Stift, Art, Aktivitätsart, Klasse, Schülergruppe, Fach, Lehrkraft,
// Wst, Von, Bis und Symbolen für Termine, Schüler*innen (Fehlzeitenverwaltung der Klasse), Schülergruppen und Berichte.
// Unterricht → Klasse zeigt dieselbe Liste für eine gewählte Klasse.
import { e, holen, leer, herunterladen, heuteIso, plusTage, montag } from '../ui.js';
import { elementWahl } from '../elementwahl.js';

const L = '/img/wu/legacy/';
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
  kontext.titel('');
  const titel = art === 'lehrer' ? `${element === ich.element ? 'Mein Unterricht' : 'Unterricht'} ${element}` : `Unterricht ${element}`;

  seite.innerHTML = `
    <div class="alt-seite">
      <div class="alt-kopf"><h1>${e(titel)}</h1><button type="button" class="alt-hilfe" title="Hilfe: Liste der Unterrichte mit Zugriff auf Termine, Schüler*innen (Fehlzeiten) und Berichte" aria-label="Hilfe">?</button></div>
      <div class="alt-karte">
        <div class="alt-filter">
          ${art === 'klasse' ? '<label><span>Klasse</span><span data-wahl></span></label>' : ''}
          <label><span>Aktivitätsart</span><select data-akt><option value="">&lt;Auswahl&gt;</option>${['Unterricht', 'Vertretung', 'Prüfung'].map(a => `<option ${a === aktivitaet ? 'selected' : ''}>${a}</option>`).join('')}</select></label>
          <label><span>Zeitraum</span><span class="d-flex gap-1"><input type="date" data-von value="${von}" aria-label="von"><input type="date" data-bis value="${bis}" aria-label="bis">
            <select data-bereich aria-label="Zeitraum">${Object.entries(BEREICHE).map(([k, [t]]) => `<option value="${k}" ${k === bereich && !vonParam ? 'selected' : ''}>${t}</option>`).join('')}${vonParam ? '<option selected>Benutzerdefiniert</option>' : ''}</select>
            <button type="button" class="alt-filterknopf" data-filtern title="Filter anwenden" aria-label="Filter anwenden"><i class="bi bi-funnel-fill"></i></button></span></label>
        </div>
        <div data-inhalt class="mt-2"></div>
      </div>
      <div class="alt-karte alt-fussleiste"><button type="button" class="ad-pille" data-bericht>Bericht</button></div>
    </div>`;
  const wahl = seite.querySelector('[data-wahl]');
  if (wahl) wahl.replaceWith(elementWahl({ eintraege: el.klassen, wert: element, platzhalter: 'Klasse wählen', beiWahl: x => kontext.navigieren(ziel(art, x)) }));
  const f = n => seite.querySelector(`[data-${n}]`).value;
  seite.querySelector('[data-bereich]').onchange = ev => BEREICHE[ev.target.value] && kontext.navigieren(`#/unterricht/${art}/${encodeURIComponent(element || '-')}/${ev.target.value}`);
  seite.querySelector('[data-filtern]').onclick = () => kontext.navigieren(ziel(art, element, bereich, f('von'), f('bis'), f('akt')));
  seite.querySelector('[data-akt]').onchange = () => kontext.navigieren(ziel(art, element, bereich, f('von'), f('bis'), f('akt')));
  const inhalt = seite.querySelector('[data-inhalt]');
  if (!element) { inhalt.innerHTML = leer('Bitte eine Klasse wählen.', 'meineunterrichte'); return; }

  // Unterrichte der Periode und die Stunden im Zeitraum (geänderte Unterrichte lila)
  const [liste, stunden] = await Promise.all([
    holen(`/api/app/unterricht?${art}=${encodeURIComponent(element)}&datum=${von}`),
    art === 'lehrer' ? holen(`/api/app/klassenbuch/stunden?von=${von}&bis=${bis}&lehrer=${encodeURIComponent(element)}`).catch(() => null) : Promise.resolve(null),
  ]);
  const geaendert = new Set((stunden?.zeilen ?? []).filter(z => z.status !== 'regulaer').map(z => z.unterricht));
  const zeilen = liste.map(u => ({ ...u, aktivitaet: geaendert.has(u.id) ? 'Vertretung' : 'Unterricht' })).filter(u => !aktivitaet || u.aktivitaet === aktivitaet || (aktivitaet === 'Prüfung' && /klausur|prüfung/i.test(u.text)));
  inhalt.innerHTML = zeilen.length ? `<div class="table-responsive"><table class="alt-liste alt-breit"><thead><tr>
      <th></th><th>Art</th><th>Aktivitätsart</th><th>Klasse</th><th>Schülergruppe</th><th>Fach</th><th>Lehrkraft</th><th>Wst</th><th>Von</th><th>Bis</th><th>Termine</th><th>Schüler</th><th>Schülergruppen</th><th>Berichte</th></tr></thead><tbody>
    ${zeilen.map(u => { const k = u.klassen[0] ?? ''; return `<tr class="${u.aktivitaet === 'Vertretung' ? 'lila' : ''}">
      <td class="mitte"><a href="#/klassenbuch/${encodeURIComponent(k || '-')}/woche" title="Klassenbuch"><img src="${L}absence-edit.png" alt="Klassenbuch"></a></td>
      <td class="mitte"><img src="${L}lesson-type.png" alt="" title="Unterricht"></td><td>${e(u.aktivitaet)}</td>
      <td>${e(u.klassen.join(', '))}</td><td>${e(u.kurs || (k ? `SG_${u.klassen.join('_')}` : ''))}</td><td>${e(u.fach)}</td><td>${e(u.lehrer.join(', '))}</td>
      <td class="mitte">${u.wochenstunden}</td><td>${datumDe(sj?.von)}</td><td>${datumDe(sj?.bis)}</td>
      <td class="mitte"><a href="#/plan/klasse/${encodeURIComponent(k)}/${von}" title="${e(u.termine.join(', ') || 'Termine im Stundenplan')}"><img src="${L}calendar.png" alt="Termine"></a> <a href="#/klassenbuch/${encodeURIComponent(k)}/verlauf" title="Lehrstoff"><img src="${L}lehrstoff.png" alt="Lehrstoff"></a></td>
      <td class="mitte"><a href="#/klassenbuch/${encodeURIComponent(k)}/fehlzeiten" title="Schüler*innen und Fehlzeiten"><img src="${L}duty-speaker.png" alt="Schüler*innen"></a></td>
      <td class="mitte"><a href="#/schueler/${encodeURIComponent(k)}" title="Schülergruppe verwalten"><img src="${L}duty-order.png" alt="Schülergruppen"></a></td>
      <td class="mitte"><a href="#/berichte/-/${encodeURIComponent(k)}/${von}/${bis}" title="Berichte"><img src="${L}lehrstoff-warning.png" alt="Berichte"></a></td></tr>`; }).join('')}
    </tbody></table></div>` : leer('Keine Unterrichte gefunden.', 'meineunterrichte');
  seite.querySelector('[data-bericht]').onclick = () => herunterladen(`unterricht-${element}-${von}-${bis}.csv`,
    ['Aktivitätsart;Klassen;Schülergruppe;Fach;Lehrkraft;Wochenstunden;Termine;Räume', ...zeilen.map(u => [u.aktivitaet, u.klassen.join(', '), u.kurs, u.fach, u.lehrer.join(', '), u.wochenstunden, u.termine.join(', '), u.raeume.join(', ')]
      .map(x => `"${String(x ?? '').replace(/"/g, '""')}"`).join(';'))].join('\r\n'));
}
