// Unterricht → Mein Unterricht im neuen Listendesign, Aufbau wie in der Vorlage: Filter (Aktivitätsart, Zeitraum von/bis, Aktuelle Woche),
// Tabelle (Bearbeiten | Art | Aktivitätsart | Klasse | Schülergruppe | Fach | Lehrkraft | Wst | Von | Bis | Termine | zusätzliche Stunden |
// Schüler | Schülergruppen | Berichte), darunter „Abwesenheiten“ und die Knöpfe „Abwesenheit melden“, „Bericht“, „Neuer Unterricht“.
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
  // Unterricht → Mein Unterricht → Berichte (Symbol ganz rechts): Berichte zum Unterricht
  if (artParam === 'berichte') return (await import('./unterrichtsberichte.js')).zeigen(seite, kontext, [elementParam, bereichParam, vonParam, bisParam]);
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
  kontext.titel(titel);
  const eigenerPlan = art === 'lehrer' && !!ich.element && element === ich.element;
  const pille = (inhalt, aktiv = false) => `<label class="nl-pille ${aktiv ? 'aktiv' : ''}">${inhalt}</label>`;
  seite.innerHTML = `
    <div class="nl-leiste">
      ${art === 'klasse' ? '<span data-wahl></span>' : ''}
      ${pille(`<select data-akt aria-label="Aktivitätsart"><option value="">Aktivitätsart</option>${['Unterricht', 'Vertretung', 'Prüfung'].map(a => `<option ${a === aktivitaet ? 'selected' : ''}>${a}</option>`).join('')}</select>`, !!aktivitaet)}
      ${pille(`<span class="me-1 text-body-secondary">Zeitraum</span><input type="date" data-von value="${von}" aria-label="Zeitraum von">`)}
      ${pille(`<input type="date" data-bis value="${bis}" aria-label="Zeitraum bis">`)}
      ${pille(`<select data-bereich aria-label="Zeitraum">${Object.entries(BEREICHE).map(([k, [t]]) => `<option value="${k}" ${k === bereich && !vonParam ? 'selected' : ''}>${t}</option>`).join('')}${vonParam ? '<option selected>Benutzerdefiniert</option>' : ''}</select>`)}
      <button type="button" class="d-none" data-filtern></button>
    </div>
    <div class="nl-tabelle" data-inhalt></div>
    ${eigenerPlan ? '<section class="mu-abwesenheiten"><h3>Abwesenheiten</h3><div data-abwesenheiten class="nl-unter">Nichts anzuzeigen</div></section>' : ''}
    <div class="mu-fuss">
      ${eigenerPlan ? '<a class="nl-knopf" href="#/melden/abwesenheit">Abwesenheit melden</a>' : ''}
      <button type="button" class="nl-knopf" data-bericht>Bericht</button>
      ${ich.kollegium ? '<a class="nl-knopf" href="#/buchen/unterricht/neu">Neuer Unterricht</a>' : ''}
    </div>`;
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
  const [liste, stunden, eigene] = await Promise.all([
    holen(`/api/app/unterricht?${art}=${encodeURIComponent(element)}&datum=${von}`),
    art === 'lehrer' ? holen(`/api/app/klassenbuch/stunden?von=${von}&bis=${bis}&lehrer=${encodeURIComponent(element)}`).catch(() => null) : Promise.resolve(null),
    // in UntPlanWeb angelegter Unterricht (Neuer Unterricht) erscheint ebenfalls in „Mein Unterricht“
    ich.kollegium ? holen('/api/app/buchen/unterricht').catch(() => []) : Promise.resolve([]),
  ]);
  const neue = eigene.filter(u => art === 'lehrer' ? u.lehrer === element : u.klassen.includes(element));
  const geaendert = new Set((stunden?.zeilen ?? []).filter(z => z.status !== 'regulaer').map(z => z.unterricht));
  const zeilen = liste.map(u => ({ ...u, aktivitaet: geaendert.has(u.id) ? 'Vertretung' : 'Unterricht' })).filter(u => !aktivitaet || u.aktivitaet === aktivitaet || (aktivitaet === 'Prüfung' && /klausur|prüfung/i.test(u.text)));
  const knopf = (href, icon, titel) => `<a class="nl-icon sichtbar" href="${href}" title="${titel}" aria-label="${titel}"><i class="bi bi-${icon}"></i></a>`;
  const sg = u => u.kurs || (u.klassen[0] ? `SG_${u.klassen.join('_')}` : '');
  inhalt.innerHTML = zeilen.length || neue.length ? `<table class="mu-tabelle"><thead><tr><th></th><th>Art</th><th>Aktivitätsart</th><th>Klasse</th><th>Schülergruppe</th><th>Fach</th><th>Lehrkraft</th>
      <th class="text-end">Wst</th><th class="mu-sortiert">Von</th><th class="mu-sortiert">Bis</th><th>Termine</th><th></th><th>Schüler</th><th>Schülergruppen</th><th>Berichte</th></tr></thead><tbody>
    ${zeilen.map(u => { const k = u.klassen[0] ?? ''; return `<tr class="${u.aktivitaet === 'Vertretung' ? 'mu-geaendert' : ''}">
      <td>${knopf(`#/klassenbuch/${encodeURIComponent(k)}/verlauf`, 'pencil', 'Bearbeiten (Lehrstoff)')}</td>
      <td><i class="bi bi-easel mu-art" title="Unterricht" aria-label="Unterricht"></i></td>
      <td>${e(u.aktivitaet)}</td>
      <td>${e(u.klassen.join(', '))}</td>
      <td class="nl-unter">${e(sg(u))}</td>
      <td>${e(u.fach)}</td><td>${e(u.lehrer.join(', '))}</td>
      <td class="text-end">${u.wochenstunden}</td>
      <td class="text-nowrap">${datumDe(sj?.von)}</td><td class="text-nowrap">${datumDe(sj?.bis)}</td>
      <td class="text-nowrap">${knopf(`#/plan/klasse/${encodeURIComponent(k)}/${von}`, 'calendar3-week', e(u.termine.join(', ') || 'Termine im Stundenplan'))}
        ${knopf(`#/klassenbuch/${encodeURIComponent(k ? `k:${k}` : '-')}/uebersicht/${heuteIso()}/-`, 'journal-text', 'Klassenbuch')}</td>
      <td class="text-nowrap">${ich.kollegium ? knopf(`#/buchen/zusatz/u${u.id}`, 'calendar-plus', 'Zusätzliche Stunden') : ''}</td>
      <td>${knopf(`#/klassenbuch/${encodeURIComponent(k)}/fehlzeiten`, 'person', 'Schüler*innen und Fehlzeiten')}</td>
      <td><button type="button" class="nl-icon sichtbar" data-schuelergruppen="${u.id}" title="${e(`Schüler*innen in der Schülergruppe ${sg(u)}`)}" aria-label="Schülergruppen"><i class="bi bi-people"></i></button></td>
      <td>${knopf(`#/unterricht/berichte/${u.id}/${von}/${bis}/${encodeURIComponent(k || '-')}`, 'file-earmark-bar-graph', 'Berichte zum Unterricht')}</td></tr>`; }).join('')}
    ${neue.map(u => `<tr class="mu-neu">
      <td>${knopf(`#/buchen/zusatz/e${u.id}`, 'pencil', 'Bearbeiten (zusätzliche Stunden)')}</td>
      <td><i class="bi bi-easel mu-art" title="Neuer Unterricht" aria-label="Neuer Unterricht"></i></td>
      <td>${e(u.zusatz?.aktivitaetsart || 'Unterricht')}</td>
      <td>${e(u.klassen.join(', '))}</td><td class="nl-unter">${e(u.gruppe)}</td><td>${e(u.fach)}</td><td>${e(u.lehrer)}</td>
      <td class="text-end">${u.wochenstunden}</td><td class="text-nowrap">${datumDe(u.zusatz?.von || sj?.von)}</td><td class="text-nowrap">${datumDe(u.zusatz?.bis || sj?.bis)}</td>
      <td></td><td class="text-nowrap">${knopf(`#/buchen/zusatz/e${u.id}`, 'calendar-plus', 'Zusätzliche Stunden')}</td><td></td><td></td><td></td></tr>`).join('')}
    </tbody></table>` : `<div class="p-3">${leer('Keine Unterrichte gefunden.', 'meineunterrichte')}</div>`;
  // Abwesenheiten der Lehrkraft im Zeitraum (eigene Abwesenheitsmeldungen); sonst „Nichts anzuzeigen“ wie in der Vorlage
  if (eigenerPlan) holen('/api/app/meldungen').then(m => {
    const a = m.filter(x => x.typ === 'absenz' && x.von <= bis && (x.bis ?? x.von) >= von);
    const ziel = seite.querySelector('[data-abwesenheiten]');
    if (ziel && a.length) ziel.innerHTML = `<ul class="mu-liste">${a.map(x => `<li>${e(datumDe(x.von))}${x.bis && x.bis !== x.von ? ` – ${e(datumDe(x.bis))}` : ''}${x.stundeVon ? `, ${x.stundeVon}.${x.stundeBis && x.stundeBis !== x.stundeVon ? `–${x.stundeBis}.` : ''} Std.` : ''} · ${e(x.grund ?? '')}${x.text ? ` · ${e(x.text)}` : ''}</li>`).join('')}</ul>`;
  }).catch(() => {});
  // Schülergruppen des Unterrichts (Leitfaden Klassenleitung, Weg 1): Zuordnung der Schüler*innen (schuelergruppen.js)
  inhalt.querySelectorAll('[data-schuelergruppen]').forEach(b => b.onclick = async () => {
    try { (await import('./schuelergruppen.js')).zuordnungDialog(Number(b.dataset.schuelergruppen)); } catch (err) { kontext.fehler(err); }
  });
  seite.querySelector('[data-bericht]').onclick = () => herunterladen(`unterricht-${element}-${von}-${bis}.csv`,
    ['Aktivitätsart;Klassen;Schülergruppe;Fach;Lehrkraft;Wochenstunden;Termine;Räume', ...zeilen.map(u => [u.aktivitaet, u.klassen.join(', '), u.kurs, u.fach, u.lehrer.join(', '), u.wochenstunden, u.termine.join(', '), u.raeume.join(', ')]
      .map(x => `"${String(x ?? '').replace(/"/g, '""')}"`).join(';'))].join('\r\n'));
}
