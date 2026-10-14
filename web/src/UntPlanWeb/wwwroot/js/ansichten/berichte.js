// Berichte: Fehlzeitenbericht (je Klasse oder alle) und Lehrstoffbericht einer Klasse – Vorschau, Druck und CSV.
import { e, icon, holen, leer, filterFeld, datumKurz, heuteIso, plusTage, herunterladen, ApiFehler } from '../ui.js';

const BERICHTE = {
  fehlzeiten: { titel: 'Fehlzeitenbericht', text: 'Fehlstunden, Fehltage, entschuldigt/unentschuldigt und Verspätungen je Schüler*in.', symbol: 'fehlzeiten' },
  lehrstoff: { titel: 'Lehrstoffbericht', text: 'Lehrstoff und Hausaufgaben je Stunde einer Klasse (Klassenbuch).', symbol: 'eintraege' },
};

export async function zeigen(seite, kontext, [berichtParam, klasseParam, vonParam, bisParam]) {
  const el = await kontext.elemente();
  const bericht = BERICHTE[berichtParam] ? berichtParam : 'fehlzeiten';
  const iso = /^\d{4}-\d{2}-\d{2}$/;
  const von = iso.test(vonParam ?? '') ? vonParam : plusTage(heuteIso(), bericht === 'lehrstoff' ? -28 : -90);
  const bis = iso.test(bisParam ?? '') ? bisParam : heuteIso();
  const klasse = klasseParam && klasseParam !== '-' ? klasseParam : (bericht === 'lehrstoff' ? el.klassen[0]?.kurz ?? '' : '');
  const ziel = (b, k, v = von, bi = bis) => `#/berichte/${b}/${encodeURIComponent(k || '-')}/${v}/${bi}`;
  kontext.titel('Berichte', `<button class="btn btn-outline-secondary" data-csv>${icon('csv')}CSV herunterladen</button><button class="btn btn-primary" data-drucken>${icon('drucken')}Drucken</button>`);
  seite.innerHTML = `
    <div class="u-kacheln nicht-drucken" style="grid-template-columns:repeat(auto-fill,minmax(300px,1fr))">${Object.entries(BERICHTE).map(([id, b]) => `<a class="u-kennzahl" href="${ziel(id, id === bericht ? klasse : '')}" style="${id === bericht ? 'box-shadow:0 0 0 2px var(--u-akzent)' : ''}">
      ${icon(b.symbol)}<span><b style="font-size:1rem">${e(b.titel)}</b><small class="fw-normal">${e(b.text)}</small></span></a>`).join('')}</div>
    <div class="u-filter nicht-drucken">
      ${filterFeld('Klasse', `<select class="form-select" data-klasse>${bericht === 'fehlzeiten' ? '<option value="">alle Klassen</option>' : ''}${el.klassen.map(k => `<option ${k.kurz === klasse ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select>`)}
      ${filterFeld('Von', `<input type="date" class="form-control" data-von value="${von}">`)}
      ${filterFeld('Bis', `<input type="date" class="form-control" data-bis value="${bis}">`)}
    </div>
    <div class="card" data-inhalt></div>`;
  const f = n => seite.querySelector(`[data-${n}]`)?.value ?? '';
  seite.querySelectorAll('.u-filter input, .u-filter select').forEach(x => x.onchange = () => kontext.navigieren(ziel(bericht, f('klasse'), f('von') || von, f('bis') || bis)));
  document.querySelector('[data-drucken]').onclick = () => window.print();
  const q = `${klasse ? `klasse=${encodeURIComponent(klasse)}&` : ''}von=${von}&bis=${bis}`;
  document.querySelector('[data-csv]').onclick = async () => {
    try {
      const antwort = await fetch(`/api/app/berichte/${bericht}?${q}&format=csv`, { credentials: 'same-origin', headers: { 'X-UntPlanWeb': '1' } });
      if (!antwort.ok) throw new ApiFehler(antwort.status, `Fehler ${antwort.status}`);
      herunterladen(`${bericht}-${klasse || 'alle'}-${von}-${bis}.csv`, await antwort.blob());
    } catch (err) { kontext.fehler(err); }
  };
  const inhalt = seite.querySelector('[data-inhalt]');
  const r = await holen(`/api/app/berichte/${bericht}?${q}`);
  const kopf = `<div class="card-header"><div>${e(BERICHTE[bericht].titel)}${klasse ? ` · Klasse ${e(klasse)}` : ' · alle Klassen'}</div>
    <div class="small text-body-secondary fw-normal">${datumKurz(r.von)}${r.von.slice(0, 4)} – ${datumKurz(r.bis)}${r.bis.slice(0, 4)} · ${e(kontext.sitzung.schule.name || '')}</div></div>`;
  if (bericht === 'lehrstoff') {
    inhalt.innerHTML = kopf + (r.zeilen.length ? `<div class="table-responsive"><table class="table table-sm table-card align-middle"><thead><tr><th>Datum</th><th>Std.</th><th>Fach</th><th>Lehrkraft</th><th>Lehrstoff</th><th>Hausaufgabe</th></tr></thead><tbody>
      ${r.zeilen.map(x => `<tr><td class="text-nowrap">${e(datumKurz(x.datum))}</td><td>${x.stunde}.</td><td class="fw-semibold">${e(x.fach)}</td><td>${e(x.lehrer)}</td><td>${e(x.thema) || '<span class="text-body-tertiary">—</span>'}</td>
        <td>${e(x.hausaufgabe)}${x.faellig ? `<div class="small text-body-secondary">bis ${datumKurz(x.faellig)}</div>` : ''}</td></tr>`).join('')}</tbody></table></div>`
      : leer('Keine Klassenbucheinträge im Zeitraum.', 'eintraege'));
    return;
  }
  const summe = k => r.zeilen.reduce((s, z) => s + z[k], 0);
  inhalt.innerHTML = kopf + (r.zeilen.length ? `<div class="table-responsive"><table class="table table-sm table-hover table-card align-middle"><thead><tr><th>Klasse</th><th>Name</th>
      <th class="text-end">Fehlstunden</th><th class="text-end">Fehltage</th><th class="text-end">entschuldigt</th><th class="text-end">unentschuldigt</th><th class="text-end">offen</th><th class="text-end">Verspätungen</th></tr></thead><tbody>
    ${r.zeilen.map(z => `<tr><td>${e(z.klasse)}</td><td class="fw-semibold">${e(z.name)}, ${e(z.vorname)}</td><td class="text-end fw-semibold">${z.stunden || ''}</td><td class="text-end">${z.tage || ''}</td>
      <td class="text-end text-success">${z.entschuldigt || ''}</td><td class="text-end text-danger">${z.unentschuldigt || ''}</td><td class="text-end">${z.offen || ''}</td>
      <td class="text-end">${z.verspaetungen ? `${z.verspaetungen} <small class="text-body-secondary">(${z.minuten} min)</small>` : ''}</td></tr>`).join('')}
    <tr class="fw-semibold"><td colspan="2">Summe</td><td class="text-end">${summe('stunden')}</td><td class="text-end">${summe('tage')}</td><td class="text-end">${summe('entschuldigt')}</td><td class="text-end">${summe('unentschuldigt')}</td><td class="text-end">${summe('offen')}</td><td class="text-end">${summe('verspaetungen')}</td></tr>
    </tbody></table></div>` : leer('Für diese Auswahl wurde keine Schülerliste übertragen.', 'fehlzeiten'));
}
