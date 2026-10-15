// Klassenbuch → Berichte wie im Original: Filter (Klasse, Schüler*in, Zeitraum), Berichte nach Gruppen (Abwesenheiten,
// Klassenbucheinträge, Arbeitsbericht, Prüfungen, Weitere) mit Anzeigen, Drucken (PDF über den Browser) und CSV.
import { e, icon, holen, leer, filterFeld, heuteIso, plusTage, herunterladen, ApiFehler, lade } from '../ui.js';

const ALIAS = { fehlzeiten: 'fehlzeiten_schueler', lehrstoff: 'arbeitsbericht_tag' };
const GRUPPENSYMBOL = { Abwesenheiten: 'abwesenheiten', Klassenbucheinträge: 'eintraege', Arbeitsbericht: 'woche', Prüfungen: 'pruefung', Weitere: 'berichte' };
const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';

export async function zeigen(seite, kontext, [berichtParam, klasseParam, vonParam, bisParam, schuelerParam, n1Param, n2Param]) {
  const el = await kontext.elemente();
  const arten = await holen('/api/app/berichte/liste');
  const art = arten.find(a => a.id === (ALIAS[berichtParam] ?? berichtParam))?.id ?? null;
  const iso = /^\d{4}-\d{2}-\d{2}$/;
  const von = iso.test(vonParam ?? '') ? vonParam : plusTage(heuteIso(), -30);
  const bis = iso.test(bisParam ?? '') ? bisParam : heuteIso();
  const klasse = klasseParam && klasseParam !== '-' ? klasseParam : '';
  const schueler = schuelerParam && schuelerParam !== '-' ? schuelerParam : '';
  const n1 = Number(n1Param) || 0, n2 = Number(n2Param) || 0;
  const ziel = (a, k = klasse, v = von, b = bis, s = schueler, x1 = n1, x2 = n2) =>
    `#/berichte/${a ?? '-'}/${encodeURIComponent(k || '-')}/${v}/${b}/${s || '-'}/${x1 || 0}/${x2 || 0}`;
  kontext.titel('Berichte', '<button type="button" class="alt-hilfe" title="PDF: Druckansicht, Seite: Vorschau, Excel: CSV-Datei" aria-label="Hilfe">?</button>');
  const schuelerListe = klasse ? await holen(`/api/app/klassenbuch/schueler?klasse=${encodeURIComponent(klasse)}`).catch(() => []) : [];
  const gruppen = [...new Set(arten.map(a => a.gruppe))];
  const parameter = a => {
    if (!a.parameter) return '';
    const s1 = `<input type="number" min="1" class="form-control form-control-sm d-inline-block" style="width:70px" data-n1="${a.id}" value="${a.id === art && n1 ? n1 : a.id === 'unentschuldigt' ? 3 : 16}">`;
    const s2 = `<input type="number" min="1" class="form-control form-control-sm d-inline-block" style="width:70px" data-n2="${a.id}" value="${a.id === art && n2 ? n2 : 4}">`;
    return `<span class="small text-body-secondary d-inline-flex align-items-center gap-1 flex-wrap">${e(a.parameter).replace('{n1}', s1).replace('{n2}', s2)}</span>`;
  };
  seite.innerHTML = `
    <div class="u-filter nicht-drucken">
      ${filterFeld('Klasse', `<select class="form-select" data-klasse><option value="">alle Klassen</option>${el.klassen.map(k => `<option ${k.kurz === klasse ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select>`)}
      ${filterFeld('Schüler*innen', `<select class="form-select" data-schueler ${klasse ? '' : 'disabled'}><option value="">- Alle -</option>${schuelerListe.map(s => `<option value="${s.id}" ${String(s.id) === schueler ? 'selected' : ''}>${e(s.name)} ${e(s.vorname)}</option>`).join('')}</select>`)}
      ${filterFeld('Von', `<input type="date" class="form-control" data-von value="${von}">`)}
      ${filterFeld('Bis', `<input type="date" class="form-control" data-bis value="${bis}">`)}
    </div>
    <div class="row g-4">
      <div class="col-xl-5 nicht-drucken">${gruppen.map(g => `<section class="br-gruppe br-alt"><h3><img src="/img/wu/legacy/lehrstoff.png" alt="">${e(g)}</h3>
        ${arten.filter(a => a.gruppe === g).map(a => `<div class="br-zeile ${a.id === art ? 'aktiv' : ''}"><div class="name">${e(a.titel)}${a.klasseNoetig ? ' <span class="small text-body-secondary fw-normal">(Klasse)</span>' : ''}</div>
          <span class="br-symbole"><button type="button" class="br-pdf" data-druck="${a.id}" title="PDF (Drucken bzw. als PDF speichern)" aria-label="${e(a.titel)} als PDF"><i class="bi bi-file-earmark-pdf-fill"></i></button>
          <button type="button" class="br-html" data-anzeigen="${a.id}" title="Anzeigen" aria-label="${e(a.titel)} anzeigen"><i class="bi bi-file-earmark-richtext-fill"></i></button>
          <button type="button" class="br-xls" data-csv="${a.id}" title="Excel (CSV)" aria-label="${e(a.titel)} als Excel"><i class="bi bi-file-earmark-excel-fill"></i></button></span>
          <span class="br-param">${parameter(a)}</span></div>`).join('')}</section>`).join('')}
        <p class="small text-body-secondary">Notfallliste, BAföG- und Fahrkostenbescheinigungen benötigen Adress- bzw. Kontaktdaten, die UntPlan nicht überträgt.</p></div>
      <div class="col-xl-7" data-vorschau>${art ? lade() : `<div class="br-vorschau">${leer('Bericht wählen und „Anzeigen“ klicken.', 'file-earmark-bar-graph')}</div>`}</div>
    </div>`;
  const f = n => seite.querySelector(`[data-${n}]`)?.value ?? '';
  const nWerte = id => [Number(seite.querySelector(`[data-n1="${id}"]`)?.value) || 0, Number(seite.querySelector(`[data-n2="${id}"]`)?.value) || 0];
  seite.querySelectorAll('.u-filter select, .u-filter input').forEach(x => x.onchange = () =>
    kontext.navigieren(ziel(art, f('klasse'), f('von') || von, f('bis') || bis, x.matches('[data-klasse]') ? '' : f('schueler'))));
  const abfrage = (id, n = nWerte(id)) => `klasse=${encodeURIComponent(klasse)}&von=${von}&bis=${bis}${schueler ? `&schueler=${schueler}` : ''}&n1=${n[0]}&n2=${n[1]}`;
  seite.querySelectorAll('[data-anzeigen]').forEach(b => b.onclick = () => { const n = nWerte(b.dataset.anzeigen); kontext.navigieren(ziel(b.dataset.anzeigen, klasse, von, bis, schueler, n[0], n[1])); });
  seite.querySelectorAll('[data-druck]').forEach(b => b.onclick = () => {
    const n = nWerte(b.dataset.druck);
    if (b.dataset.druck === art) { window.print(); return; }
    sessionStorage.setItem('untplan:berichtdruck', '1');
    kontext.navigieren(ziel(b.dataset.druck, klasse, von, bis, schueler, n[0], n[1]));
  });
  seite.querySelectorAll('[data-csv]').forEach(b => b.onclick = async () => {
    try {
      const antwort = await fetch(`/api/app/berichte/erstellen/${b.dataset.csv}?${abfrage(b.dataset.csv)}&format=csv`, { credentials: 'same-origin', headers: { 'X-UntPlanWeb': '1' } });
      if (!antwort.ok) throw new ApiFehler(antwort.status, (await antwort.json().catch(() => null))?.fehler ?? `Fehler ${antwort.status}`);
      herunterladen(`${b.dataset.csv}-${klasse || 'alle'}-${von}-${bis}.csv`, await antwort.blob());
    } catch (err) { kontext.fehler(err); }
  });
  if (!art) return;

  const vorschau = seite.querySelector('[data-vorschau]');
  let r;
  try { r = await holen(`/api/app/berichte/erstellen/${art}?${abfrage(art, [n1, n2])}`); }
  catch (err) { vorschau.innerHTML = `<div class="br-vorschau p-3">${e(err.message)}</div>`; return; }
  const kopf = `<div class="br-druckkopf"><b>${e(kontext.sitzung.schule.name || '')}</b> · ${e(r.titel)}${klasse ? ` · Klasse ${e(klasse)}` : ''} · ${datumDe(r.von)} – ${datumDe(r.bis)}</div>`;
  const tabelle = zeilen => `<div class="table-responsive"><table class="table table-sm table-hover table-card align-middle mb-0"><thead><tr>${r.spalten.map(s => `<th>${e(s)}</th>`).join('')}</tr></thead><tbody>
    ${zeilen.map(z => `<tr>${z.map((x, i) => `<td class="${i === 0 || r.spalten[i] === 'Schüler*in' ? 'fw-semibold' : ''}">${e(x ?? '')}</td>`).join('')}</tr>`).join('')}</tbody></table></div>`;
  if (art === 'entschuldigungsschreiben') {
    // je Schüler*in eine Seite mit Unterschriftsfeld
    const proSchueler = new Map();
    r.zeilen.forEach(z => proSchueler.set(z[0], [...(proSchueler.get(z[0]) ?? []), z]));
    vorschau.innerHTML = `<div class="br-vorschau"><div class="card-header d-flex align-items-center nicht-drucken"><h2>${e(r.titel)}</h2><span class="ms-auto small text-body-secondary">${proSchueler.size} Schüler*innen</span></div>
      ${proSchueler.size ? [...proSchueler].map(([name, zeilen]) => `<div class="p-3 br-seite border-bottom">${kopf}<h3 class="h6 fw-bold mt-2">Entschuldigung für ${e(name)} (${e(zeilen[0][1])})</h3>
        <p class="small mb-2">Hiermit entschuldige ich das Fehlen meines Kindes zu folgenden Zeiten:</p>
        <table class="table table-sm mb-3"><thead><tr><th>Datum</th><th>Stunden</th><th>Fächer</th><th>Grund</th></tr></thead><tbody>${zeilen.map(z => `<tr><td>${e(z[2])}</td><td>${e(z[3])}</td><td>${e(z[4])}</td><td>${e(z[5])}</td></tr>`).join('')}</tbody></table>
        <div class="d-flex gap-4 small pt-4"><span class="border-top pt-1 flex-fill">Datum</span><span class="border-top pt-1 flex-fill">Unterschrift Erziehungsberechtigte</span></div></div>`).join('')
        : leer('Keine offenen Fehlstunden im Zeitraum.', 'check2-circle')}</div>`;
  } else {
    vorschau.innerHTML = `<div class="br-vorschau">${kopf}<div class="card-header d-flex flex-wrap align-items-center gap-2"><h2>${e(r.titel)}</h2>
        <span class="small text-body-secondary">${klasse ? `Klasse ${e(klasse)} · ` : ''}${datumDe(r.von)} – ${datumDe(r.bis)} · ${r.zeilen.length} Zeilen</span>
        <button class="btn btn-sm btn-outline-secondary ms-auto nicht-drucken" data-jetzt-drucken>${icon('drucken')}Drucken</button></div>
      ${r.hinweis ? `<div class="px-3 pt-2 small text-body-secondary">${e(r.hinweis)}</div>` : ''}
      ${r.zeilen.length ? tabelle(r.zeilen) : leer('Keine Daten für diese Auswahl.', 'file-earmark')}</div>`;
    vorschau.querySelector('[data-jetzt-drucken]').onclick = () => window.print();
  }
  if (sessionStorage.getItem('untplan:berichtdruck')) { sessionStorage.removeItem('untplan:berichtdruck'); setTimeout(() => window.print(), 300); }
}
