// Unterricht → Mein Unterricht → Berichte (Symbol ganz rechts): „Berichte zum Unterricht“ im neuen Listendesign – Kopf mit Klasse,
// Lehrkraft und Fach, Zeitraum (Aktuelle Woche, Aktuelles Schuljahr, Datumsbereich) und je Bericht Anzeigen/PDF/CSV:
// Arbeitsbericht, Fehlzeiten pro Unterricht pro Schüler*in (75 %-Regel vor der abschließenden Leistungsfeststellung),
// Fehlzeiten pro Unterricht pro Stunde, Fehlstundenmatrix (mit Haken „ohne Einträge“).
import { e, holen, leer, heuteIso, plusTage, montag, herunterladen, ApiFehler, lade, pdfAusrichtung } from '../ui.js';

const BERICHTE = [
  { id: 'arbeitsbericht_unterricht', titel: 'Arbeitsbericht', symbol: 'journal-check' },
  { id: 'fehlzeiten_unterricht_schueler', titel: 'Fehlzeiten pro Unterricht pro Schüler*in', symbol: 'person-x' },
  { id: 'fehlzeiten_unterricht_stunde', titel: 'Fehlzeiten pro Unterricht pro Stunde', symbol: 'clock-history' },
  { id: 'fehlstundenmatrix', titel: 'Fehlstundenmatrix', symbol: 'grid-3x3', haken: { name: 'ohne', titel: 'ohne Einträge' } },
  { id: 'schueler_unterricht', titel: 'Schüler*innen im Unterricht', text: 'Liste der Schüler*innen des Unterrichts.', symbol: 'people' },
];
const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';
const holeDatei = async url => {
  const antwort = await fetch(url, { credentials: 'same-origin', headers: { 'X-UntPlanWeb': '1' } });
  if (!antwort.ok) throw new ApiFehler(antwort.status, (await antwort.json().catch(() => null))?.fehler ?? `Fehler ${antwort.status}`);
  return antwort.blob();
};

export async function zeigen(seite, kontext, [idParam, vonParam, bisParam, klasseParam]) {
  const id = Number(idParam) || 0;
  const sj = kontext.schuljahr ?? {};
  const iso = /^\d{4}-\d{2}-\d{2}$/;
  const von = iso.test(vonParam ?? '') ? vonParam : montag(heuteIso());
  const bis = iso.test(bisParam ?? '') ? bisParam : plusTage(montag(heuteIso()), 6);
  const klasse = klasseParam && klasseParam !== '-' ? decodeURIComponent(klasseParam) : '';
  const ziel = (v, b) => `#/unterricht/berichte/${id}/${v}/${b}/${encodeURIComponent(klasse || '-')}`;
  const liste = await holen(`/api/app/unterricht?${klasse ? `klasse=${encodeURIComponent(klasse)}&` : ''}datum=${von}`).catch(() => []);
  const u = liste.find(x => x.id === id);
  kontext.titel('Berichte zum Unterricht', `<a class="nl-knopf" href="#/unterricht"><i class="bi bi-arrow-left"></i>Zurück</a>`);
  const haken = {};
  const bereiche = { woche: ['Aktuelle Woche', [montag(heuteIso()), plusTage(montag(heuteIso()), 6)]], schuljahr: ['Aktuelles Schuljahr', [sj.von || von, sj.bis || bis]],
    bisWoche: ['Datumsbereich: Schuljahresbeginn bis Ende der aktuellen Woche', [sj.von || von, plusTage(montag(heuteIso()), 6)]] };
  const bereich = Object.entries(bereiche).find(([, [, [v, b]]]) => v === von && b === bis)?.[0] ?? '';
  seite.innerHTML = `
    <div class="u-kopfteil">
      <div class="nl-leiste">
        <div class="ub-kopf"><div class="nl-name">${u ? `Klasse ${e(u.klassen.join(', '))}, ${e(u.fach)}` : `Unterricht ${id}`}</div>
          <div class="nl-unter">${u ? `Lehrkraft ${e(u.lehrer.join(', '))}${u.kurs ? `, Schülergruppe ${e(u.kurs)}` : ''}` : ''}</div></div>
        <label class="nl-pille"><span class="me-1 text-body-secondary">Von</span><input type="date" data-von value="${von}"></label>
        <label class="nl-pille"><span class="me-1 text-body-secondary">Bis</span><input type="date" data-bis value="${bis}"></label>
        <label class="nl-pille ${bereich ? 'aktiv' : ''}"><select data-bereich aria-label="Zeitraum">${bereich ? '' : '<option value="">Datumsbereich</option>'}${Object.entries(bereiche).map(([k, [t]]) => `<option value="${k}" ${k === bereich ? 'selected' : ''}>${e(t)}</option>`).join('')}</select></label>
      </div>
    </div>
    <div data-vorschau class="br-vorschau-bereich"></div>
    <div class="br-liste" role="list">${BERICHTE.map(b => `<div class="br-reihe" role="listitem">
      <span class="br-symbol rot"><i class="bi bi-${b.symbol}" aria-hidden="true"></i></span>
      <button type="button" class="br-reihe-text" data-anzeigen="${b.id}"><b>${e(b.titel)}</b></button>
      <span class="br-reihe-tags">${b.haken ? `<label class="form-check m-0"><input type="checkbox" class="form-check-input" data-haken="${b.id}"><span class="form-check-label small">${e(b.haken.titel)}</span></label>` : ''}</span>
      <span class="br-reihe-aktionen">
        <button type="button" class="br-knopf" data-anzeigen="${b.id}" title="Anzeigen"><i class="bi bi-eye"></i><span>Anzeigen</span></button>
        <button type="button" class="br-knopf" data-pdf="${b.id}" title="Als PDF herunterladen"><i class="bi bi-file-earmark-pdf"></i><span>PDF</span></button>
        <button type="button" class="br-knopf" data-csv="${b.id}" title="Als CSV herunterladen"><i class="bi bi-filetype-csv"></i><span>CSV</span></button>
      </span></div>`).join('')}</div>`;
  const f = n => seite.querySelector(`[data-${n}]`).value;
  seite.querySelectorAll('[data-von], [data-bis]').forEach(x => x.onchange = () => kontext.navigieren(ziel(f('von') || von, f('bis') || bis)));
  seite.querySelector('[data-bereich]').onchange = ev => { const b = bereiche[ev.target.value]; if (b) kontext.navigieren(ziel(b[1][0], b[1][1])); };
  seite.querySelectorAll('[data-haken]').forEach(c => c.onchange = () => { haken[c.dataset.haken] = c.checked; });
  const abfrage = art => `von=${von}&bis=${bis}&unterricht=${id}${BERICHTE.find(b => b.id === art)?.haken && haken[art] ? `&${BERICHTE.find(b => b.id === art).haken.name}=1` : ''}`;
  const vorschau = seite.querySelector('[data-vorschau]');

  async function anzeigen(art) {
    const b = BERICHTE.find(x => x.id === art);
    vorschau.innerHTML = lade();
    let r;
    try { r = await holen(`/api/app/berichte/erstellen/${art}?${abfrage(art)}`); }
    catch (err) { vorschau.innerHTML = `<div class="br-vorschau s-karte p-3">${e(err.message)}</div>`; return; }
    const arten = r.arten ?? [];
    vorschau.innerHTML = `<div class="br-vorschau s-karte"><header class="br-vkopf"><span class="br-symbol rot"><i class="bi bi-${b.symbol}"></i></span>
        <div><h2>${e(r.titel)}</h2><span>${datumDe(r.von)} – ${datumDe(r.bis)}</span></div>
        <div class="br-vaktionen"><button type="button" class="nl-icon sichtbar" data-zu title="Vorschau schließen" aria-label="Vorschau schließen"><i class="bi bi-x-lg"></i></button></div></header>
      ${r.hinweis ? `<div class="br-vhinweis"><i class="bi bi-info-circle"></i> ${e(r.hinweis)}</div>` : ''}
      ${r.zeilen.length ? `<div class="table-responsive"><table class="table table-sm table-hover table-card align-middle mb-0 br-tabelle ${arten.length ? 'br-gegliedert' : ''}">
        <thead><tr>${r.spalten.map(s => `<th>${e(s)}</th>`).join('')}</tr></thead>
        <tbody>${r.zeilen.map((z, n) => `<tr class="${arten[n] ? `br-${arten[n]}` : ''}">${z.map(x => `<td>${e(x ?? '').replace(/\n/g, '<br>')}</td>`).join('')}</tr>`).join('')}</tbody></table></div>`
        : leer('Keine Daten für diese Auswahl.', 'file-earmark')}</div>`;
    vorschau.querySelector('[data-zu]').onclick = () => { vorschau.innerHTML = ''; };
  }
  async function exportieren(art, format, knopf) {
    const ausrichtung = format === 'pdf' ? await pdfAusrichtung('quer', 'bericht') : null;
    if (format === 'pdf' && !ausrichtung) return;
    knopf.disabled = true;
    try { herunterladen(`${art}-${id}-${von}-${bis}.${format}`, await holeDatei(`/api/app/berichte/erstellen/${art}?${abfrage(art)}&format=${format}${ausrichtung ? `&ausrichtung=${ausrichtung}` : ''}`)); }
    catch (err) { kontext.fehler(err); }
    finally { knopf.disabled = false; }
  }
  seite.querySelectorAll('[data-anzeigen]').forEach(b => b.onclick = () => anzeigen(b.dataset.anzeigen));
  seite.querySelectorAll('[data-pdf]').forEach(b => b.onclick = () => exportieren(b.dataset.pdf, 'pdf', b));
  seite.querySelectorAll('[data-csv]').forEach(b => b.onclick = () => exportieren(b.dataset.csv, 'csv', b));
}
