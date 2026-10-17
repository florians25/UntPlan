// Klassenbuch → Berichte im nl-design: Filterleiste (Klasse, Schüler*in, Zeitraum), Reiter je Berichtsgruppe, Berichte als ruhige Liste
// (Symbol, Titel, Beschreibung, rechts Anzeigen/PDF/CSV); Parameter im Seitenpanel. Vorschau oberhalb der Liste.
import { e, icon, holen, leer, heuteIso, plusTage, herunterladen, ApiFehler, lade, pdfAusrichtung, druckenMitAusrichtung, suchfeldMitFilter, suchfeldMitFilterBinden } from '../ui.js';
import { kbDialog as dialog } from '../kbdialog.js';

const ALIAS = { fehlzeiten: 'fehlzeiten_schueler', lehrstoff: 'arbeitsbericht_tag' };
const GRUPPEN = {
  Abwesenheiten: { symbol: 'person-x', farbe: 'rot' },
  Klassenbucheinträge: { symbol: 'journal-text', farbe: 'blau' },
  Arbeitsbericht: { symbol: 'journal-check', farbe: 'gruen' },
  Prüfungen: { symbol: 'file-earmark-check', farbe: 'gelb' },
  Weitere: { symbol: 'grid', farbe: 'lila' },
};
const BESCHREIBUNG = {
  fehlzeiten_schueler: 'Fehlstunden, Fehltage und Entschuldigungsstatus je Schüler*in im gewählten Zeitraum.',
  fehlzeiten_klasse: 'Summen der Fehlzeiten je Klasse – geeignet für Konferenzen und Zeugnisse.',
  fehlzeiten_fach: 'Fehlstunden aufgeschlüsselt nach Fächern.',
  fehltage_klasse: 'Fehltage je Klasse und Schüler*in mit Datum, Stunden und Status, Summen je Schüler*in (mit Schultagen) und je Klasse.',
  verspaetungen: 'Alle Verspätungen und vorzeitigen Abgänge mit Minuten.',
  verspaetungen_auffaellig: 'Schüler*innen, die im Zeitraum auffällig oft zu spät kamen.',
  abwesenheitsueberschreitung: 'Wer in einem gleitenden Zeitraum mehr Fehlstunden als erlaubt hat.',
  abwesenheiten: 'Abwesenheiten als Zeiträume mit Grund, Text und Status.',
  anwesenheiten: 'Anwesenheit je Schüler*in über alle erfassten Stunden.',
  unentschuldigt: 'Zusammenhängende Abwesenheiten ohne Entschuldigung je Schüler*in mit Beginn, Ende, Tagen, Stunden und Grund; Summen je Klasse.',
  statusuebersicht: 'Fehlzeiten gruppiert nach offen, entschuldigt und nicht entschuldigt.',
  entschuldigungsschreiben: 'Vordruck je Schüler*in mit den offenen Fehlstunden und Unterschriftsfeld.',
  klassenbucheintraege: 'Klassenbucheinträge wie Lob, Vermerke und Ordnungsmaßnahmen je Klasse.',
  klassenbucheintraege_schueler: 'Klassenbucheinträge je Schüler*in.',
  arbeitsbericht_tag: 'Lehrstoff und Hausaufgaben einer Klasse Tag für Tag.',
  tagesuebersicht: 'Alle Stunden der Klasse je Tag mit Lehrstoff und Abwesenheiten.',
  wochenuebersicht: 'Wochenweise Übersicht der Stunden und Einträge einer Klasse.',
  pruefungen: 'Prüfungen und Klassenarbeiten im Zeitraum.',
  befreiungen: 'Befreiungen vom Unterricht mit Fach und Zeitraum.',
  klassendienste: 'Eingeteilte Klassendienste im Zeitraum.',
};
const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';
const holeDatei = async url => {
  const antwort = await fetch(url, { credentials: 'same-origin', headers: { 'X-UntPlanWeb': '1' } });
  if (!antwort.ok) throw new ApiFehler(antwort.status, (await antwort.json().catch(() => null))?.fehler ?? `Fehler ${antwort.status}`);
  return antwort.blob();
};

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
  const schuelerVon = k => k ? holen(`/api/app/klassenbuch/schueler?klasse=${encodeURIComponent(k)}`).catch(() => []) : Promise.resolve([]);
  const schuelerListe = await schuelerVon(klasse);
  const gruppen = [...new Set(arten.map(a => a.gruppe))];
  let gruppe = 'alle';
  try { gruppe = sessionStorage.getItem('untplan:berichtgruppe') || 'alle'; } catch { /* privat */ }
  if (gruppe !== 'alle' && !gruppen.includes(gruppe)) gruppe = 'alle';
  let suche = '';
  const vorgabe = (a, n) => n === 1 ? (a.id === art && n1 ? n1 : a.id === 'unentschuldigt' ? 1 : 16) : (a.id === art && n2 ? n2 : 4);
  const abfrage = (p) => `klasse=${encodeURIComponent(p.klasse)}&von=${p.von}&bis=${p.bis}${p.schueler ? `&schueler=${p.schueler}` : ''}&n1=${p.n1}&n2=${p.n2}`;
  const standard = a => ({ klasse, schueler, von, bis, n1: vorgabe(a, 1), n2: vorgabe(a, 2) });

  kontext.titel('Berichte', '', klasse ? `Klasse ${klasse}` : 'alle Klassen');
  seite.innerHTML = `<div class="br-seite">
    <!-- weißer Kopf (app.js): Suche und Parameter der Berichte oben, Vorschau und Liste darunter auf grauem Grund -->
    <div class="u-kopfteil br-kopf nicht-drucken">
      <div class="u-sf-leiste" data-gruppen>${suchfeldMitFilter({ platzhalter: 'Bericht suchen', label: 'Bericht suchen', filter: [
        { name: 'gruppe', label: 'Berichtsgruppe', alle: `Alle Berichte (${arten.length})`, standard: 'alle', wert: gruppe, optionen: gruppen.map(g => [g, `${g} (${arten.filter(a => a.gruppe === g).length})`]) },
      ] })}</div>
      <div class="kd-filter nicht-drucken">
        <label class="kd-feld"><span>Klasse</span><select data-klasse><option value="">Alle Klassen</option>${el.klassen.map(k => `<option ${k.kurz === klasse ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select></label>
        <label class="kd-feld"><span>Schüler*in</span><select data-schueler ${klasse ? '' : 'disabled'}><option value="">Alle</option>${schuelerListe.map(s => `<option value="${s.id}" ${String(s.id) === schueler ? 'selected' : ''}>${e(s.name)} ${e(s.vorname)}</option>`).join('')}</select></label>
        <label class="kd-feld"><span>Von</span><input type="date" data-von value="${von}"></label>
        <label class="kd-feld"><span>Bis</span><input type="date" data-bis value="${bis}"></label>
      </div>
    </div>
    <div data-vorschau class="br-vorschau-bereich">${art ? lade() : ''}</div>
    <div class="br-raster nicht-drucken" data-raster></div>
    <p class="br-fussnote nicht-drucken"><i class="bi bi-info-circle"></i> Notfallliste, BAföG- und Fahrkostenbescheinigungen benötigen Adress- bzw. Kontaktdaten, die UntPlan nicht überträgt.</p>
  </div>`;

  const raster = seite.querySelector('[data-raster]');
  // je Bericht eine ruhige Zeile: Symbol, Titel und Beschreibung (öffnet die Parameter), Hinweise, rechts die Aktionen
  const karte = a => {
    const g = GRUPPEN[a.gruppe] ?? GRUPPEN.Weitere;
    const fehltKlasse = a.klasseNoetig && !klasse;
    return `<div class="br-reihe ${a.id === art ? 'aktiv' : ''}" data-karte="${a.id}" role="listitem">
      <span class="br-symbol ${g.farbe}"><i class="bi bi-${g.symbol}" aria-hidden="true"></i></span>
      <button type="button" class="br-reihe-text" data-panel="${a.id}" aria-label="${e(a.titel)}: Parameter und Export">
        <b>${e(a.titel)}</b><span>${e(BESCHREIBUNG[a.id] ?? a.gruppe)}</span></button>
      <span class="br-reihe-tags">${a.klasseNoetig ? `<span class="s-label ${fehltKlasse ? 'orange' : ''}">${fehltKlasse ? 'Klasse wählen' : 'je Klasse'}</span>` : ''}
        ${a.parameter ? `<span class="s-label">${e(a.parameter.replace('{n1}', vorgabe(a, 1)).replace('{n2}', vorgabe(a, 2)))}</span>` : ''}</span>
      <span class="br-reihe-aktionen">
        <button type="button" class="br-knopf" data-anzeigen="${a.id}" ${fehltKlasse ? 'disabled' : ''} title="Anzeigen"><i class="bi bi-eye"></i><span>Anzeigen</span></button>
        <button type="button" class="br-knopf" data-pdf="${a.id}" ${fehltKlasse ? 'disabled' : ''} title="Als PDF herunterladen"><i class="bi bi-file-earmark-pdf"></i><span>PDF</span></button>
        <button type="button" class="br-knopf" data-csv="${a.id}" ${fehltKlasse || a.id === 'entschuldigungsschreiben' ? 'disabled' : ''} title="Als CSV herunterladen"><i class="bi bi-filetype-csv"></i><span>CSV</span></button>
      </span></div>`;
  };
  const rasterZeichnen = () => {
    const q = suche.trim().toLowerCase();
    const sicht = arten.filter(a => (gruppe === 'alle' || a.gruppe === gruppe) && (!q || `${a.titel} ${BESCHREIBUNG[a.id] ?? ''}`.toLowerCase().includes(q)));
    raster.innerHTML = sicht.length ? (gruppe === 'alle' && !q
      ? gruppen.map(g => `<section class="br-abschnitt"><h2>${e(g)}</h2><div class="br-liste" role="list">${arten.filter(a => a.gruppe === g).map(karte).join('')}</div></section>`).join('')
      : `<div class="br-liste" role="list">${sicht.map(karte).join('')}</div>`)
      : `<div class="s-leer"><span class="s-leer-symbol"><i class="bi bi-search"></i></span><b>Kein Bericht gefunden</b><p>Für „${e(suche)}“ gibt es keinen passenden Bericht.</p></div>`;
    raster.querySelectorAll('[data-panel]').forEach(b => b.onclick = () => panel(arten.find(a => a.id === b.dataset.panel)));
    raster.querySelectorAll('[data-anzeigen]').forEach(b => b.onclick = () => { const a = arten.find(x => x.id === b.dataset.anzeigen); anzeigen(a, standard(a)); });
    raster.querySelectorAll('[data-pdf]').forEach(b => b.onclick = () => { const a = arten.find(x => x.id === b.dataset.pdf); exportieren(a, standard(a), 'pdf', b); });
    raster.querySelectorAll('[data-csv]').forEach(b => b.onclick = () => { const a = arten.find(x => x.id === b.dataset.csv); exportieren(a, standard(a), 'csv', b); });
  };
  rasterZeichnen();
  // gemeinsames Suchfeld mit Filter: Freitext und Berichtsgruppe als Dropdown im Suchfeld
  suchfeldMitFilterBinden(seite.querySelector('[data-gruppen]'), {
    beiSuche: t => { suche = t; rasterZeichnen(); },
    beiFilter: (_, wert) => {
      gruppe = wert || 'alle';
      try { sessionStorage.setItem('untplan:berichtgruppe', gruppe); } catch { /* privat */ }
      rasterZeichnen();
    },
  });

  const f = n => seite.querySelector(`[data-${n}]`)?.value ?? '';
  seite.querySelectorAll('.kd-filter select, .kd-filter input[type=date]').forEach(x => x.onchange = () =>
    kontext.navigieren(ziel(art, f('klasse'), f('von') || von, f('bis') || bis, x.matches('[data-klasse]') ? '' : f('schueler'))));

  function anzeigen(a, p) {
    if (a.klasseNoetig && !p.klasse) { kontext.fehler(new Error('Für diesen Bericht bitte eine Klasse wählen.')); return; }
    document.querySelector('.br-dialog')?.closest('.kbd-overlay')?.remove();
    kontext.navigieren(ziel(a.id, p.klasse, p.von, p.bis, p.schueler, a.parameter ? p.n1 : 0, a.parameter ? p.n2 : 0));
  }
  async function exportieren(a, p, format, knopf) {
    if (a.klasseNoetig && !p.klasse) { kontext.fehler(new Error('Für diesen Bericht bitte eine Klasse wählen.')); return; }
    if (a.id === 'entschuldigungsschreiben') {
      // Entschuldigungsschreiben: Druckansicht (je Schüler*in eine Seite)
      document.querySelector('.br-dialog')?.closest('.kbd-overlay')?.remove();
      if (a.id === art) { druckenMitAusrichtung('hoch', 'entschuldigungsschreiben'); return; }
      sessionStorage.setItem('untplan:berichtdruck', '1');
      kontext.navigieren(ziel(a.id, p.klasse, p.von, p.bis, p.schueler, p.n1, p.n2));
      return;
    }
    // PDF: Hoch- oder Querformat wählen
    const ausrichtung = format === 'pdf' ? await pdfAusrichtung('quer', 'bericht') : null;
    if (format === 'pdf' && !ausrichtung) return;
    if (knopf) knopf.disabled = true;
    try { herunterladen(`${a.id}-${p.klasse || 'alle'}-${p.von}-${p.bis}.${format}`, await holeDatei(`/api/app/berichte/erstellen/${a.id}?${abfrage(p)}&format=${format}${ausrichtung ? `&ausrichtung=${ausrichtung}` : ''}`)); }
    catch (err) { kontext.fehler(err); }
    finally { if (knopf) knopf.disabled = false; }
  }

  /** Berichtsdialog (gemeinsame Modal-Komponente): Beschreibung, Parameter (Klasse, Schüler*in, Zeitraum, Schwellenwerte), Fuß mit CSV, PDF, Anzeigen. */
  async function panel(a) {
    document.querySelector('.br-dialog')?.closest('.kbd-overlay')?.remove();
    const g = GRUPPEN[a.gruppe] ?? GRUPPEN.Weitere;
    const p = standard(a);
    const param = a.parameter ? e(a.parameter)
      .replace('{n1}', `<input type="number" min="1" class="form-control br-zahl" data-p-n1 value="${p.n1}" aria-label="Wert 1">`)
      .replace('{n2}', `<input type="number" min="1" class="form-control br-zahl" data-p-n2 value="${p.n2}" aria-label="Wert 2">`) : '';
    // gemeinsame Modal-Komponente des Klassenbuchs (Kopf mit Titel und X, Fuß rechts mit Hauptknopf „Anzeigen“)
    const h = dialog({ titel: a.titel, breite: 'm', klasse: 'br-dialog',
      inhalt: `<div class="br-dialog-gruppe"><span class="br-symbol ${g.farbe}"><i class="bi bi-${g.symbol}" aria-hidden="true"></i></span><small>${e(a.gruppe)}</small></div>
        <div class="br-panel-rumpf">
          <p class="br-panel-text">${e(BESCHREIBUNG[a.id] ?? '')}</p>
          <label class="kd-dfeld"><span>Klasse${a.klasseNoetig ? ' <b class="text-danger">*</b>' : ''}</span><select class="form-select" data-p-klasse>${a.klasseNoetig ? '' : '<option value="">Alle Klassen</option>'}${el.klassen.map(k => `<option ${k.kurz === (p.klasse || (a.klasseNoetig ? el.klassen[0]?.kurz : '')) ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select></label>
          <label class="kd-dfeld"><span>Schüler*in</span><select class="form-select" data-p-schueler><option value="">Alle</option></select></label>
          <div class="kd-dialog-reihe zwei">
            <label class="kd-dfeld"><span>Von</span><input type="date" class="form-control" data-p-von value="${p.von}"></label>
            <label class="kd-dfeld"><span>Bis</span><input type="date" class="form-control" data-p-bis value="${p.bis}"></label>
          </div>
          <div class="kd-schnell"><button type="button" class="s-pille" data-zeit="30">30 Tage</button><button type="button" class="s-pille" data-zeit="hj">Halbjahr</button><button type="button" class="s-pille" data-zeit="sj">Schuljahr</button></div>
          ${param ? `<div class="kd-dfeld"><span>Schwellenwert</span><div class="br-param">${param}</div></div>` : ''}
        </div>`,
      fuss: `<button type="button" class="nl-knopf" data-p-csv ${a.id === 'entschuldigungsschreiben' ? 'disabled' : ''}><i class="bi bi-filetype-csv"></i>CSV</button>
        <button type="button" class="nl-knopf" data-p-pdf><i class="bi bi-file-earmark-pdf"></i>PDF</button>
        <button type="button" class="nl-knopf nw-primaer" data-p-anzeigen><i class="bi bi-eye"></i>Anzeigen</button>` });
    const q = x => h.querySelector(x);
    window.addEventListener('hashchange', () => h.schliessen(), { once: true });
    const schuelerLaden = async () => {
      const k = q('[data-p-klasse]').value;
      const liste = await schuelerVon(k);
      q('[data-p-schueler]').innerHTML = `<option value="">Alle</option>${liste.map(s => `<option value="${s.id}" ${String(s.id) === p.schueler && k === klasse ? 'selected' : ''}>${e(s.name)} ${e(s.vorname)}</option>`).join('')}`;
      q('[data-p-schueler]').disabled = !k;
    };
    q('[data-p-klasse]').onchange = schuelerLaden;
    schuelerLaden();
    const sj = kontext.schuljahr;
    h.querySelectorAll('[data-zeit]').forEach(b => b.onclick = () => {
      const heute = heuteIso();
      const z = b.dataset.zeit;
      const start = z === '30' ? plusTage(heute, -30) : z === 'sj' ? (sj.von || plusTage(heute, -180)) : (Number(heute.slice(5, 7)) >= 2 && Number(heute.slice(5, 7)) < 8 ? `${heute.slice(0, 4)}-02-01` : (sj.von || plusTage(heute, -180)));
      q('[data-p-von]').value = start;
      q('[data-p-bis]').value = heute;
    });
    const werte = () => ({ klasse: q('[data-p-klasse]').value, schueler: q('[data-p-schueler]').value, von: q('[data-p-von]').value || von, bis: q('[data-p-bis]').value || bis,
      n1: Number(q('[data-p-n1]')?.value) || p.n1, n2: Number(q('[data-p-n2]')?.value) || p.n2 });
    q('[data-p-anzeigen]').onclick = () => anzeigen(a, werte());
    q('[data-p-pdf]').onclick = ev => exportieren(a, werte(), 'pdf', ev.currentTarget);
    q('[data-p-csv]').onclick = ev => exportieren(a, werte(), 'csv', ev.currentTarget);
    setTimeout(() => q('[data-p-klasse]').focus(), 50);
  }

  if (!art) return;
  const vorschau = seite.querySelector('[data-vorschau]');
  const a = arten.find(x => x.id === art);
  const p = { klasse, schueler, von, bis, n1: vorgabe(a, 1), n2: vorgabe(a, 2) };
  let r;
  try { r = await holen(`/api/app/berichte/erstellen/${art}?${abfrage(p)}`); }
  catch (err) { vorschau.innerHTML = `<div class="br-vorschau s-karte p-3">${e(err.message)}</div>`; return; }
  const kopf = `<div class="br-druckkopf"><b>${e(kontext.sitzung.schule.name || '')}</b> · ${e(r.titel)}${klasse ? ` · Klasse ${e(klasse)}` : ''} · ${datumDe(r.von)} – ${datumDe(r.bis)}</div>`;
  const g = GRUPPEN[a.gruppe] ?? GRUPPEN.Weitere;
  const vkopf = zusatz => `<header class="br-vkopf nicht-drucken"><span class="br-symbol ${g.farbe}"><i class="bi bi-${g.symbol}"></i></span>
      <div><h2>${e(r.titel)}</h2><span>${klasse ? `Klasse ${e(klasse)} · ` : ''}${datumDe(r.von)} – ${datumDe(r.bis)} · ${zusatz}</span></div>
      <div class="br-vaktionen">${art !== 'entschuldigungsschreiben' ? '<button type="button" class="btn btn-outline-secondary s-knopf" data-v-pdf><i class="bi bi-file-earmark-pdf"></i>PDF</button><button type="button" class="btn btn-outline-secondary s-knopf" data-v-csv><i class="bi bi-filetype-csv"></i>CSV</button>' : ''}
        <button type="button" class="btn btn-outline-secondary s-knopf" data-v-drucken>${icon('drucken')}Drucken</button>
        <a class="br-vzu" href="${ziel(null)}" title="Vorschau schließen" aria-label="Vorschau schließen"><i class="bi bi-x-lg"></i></a></div></header>`;
  // gegliederte Berichte (r.arten): Gruppenkopf über die ganze Breite, Zwischensumme je Schüler*in, Summe je Klasse; Zahlenspalten rechtsbündig
  const zeilenArten = r.arten ?? null;
  const zahlSpalte = r.spalten.map((_, i) => r.zeilen.some((z, n) => !zeilenArten?.[n] && z[i] !== '' && z[i] != null)
    && r.zeilen.every((z, n) => zeilenArten?.[n] || z[i] === '' || z[i] == null || typeof z[i] === 'number'));
  const tabelle = zeilen => `<div class="table-responsive"><table class="table table-sm table-hover table-card align-middle mb-0 br-tabelle ${zeilenArten ? 'br-gegliedert' : ''}"><thead><tr>${r.spalten.map((s, i) => `<th class="${zeilenArten && zahlSpalte[i] ? 'text-end' : ''}">${e(s)}</th>`).join('')}</tr></thead><tbody>
    ${zeilen.map((z, n) => zeilenArten?.[n] === 'gruppe'
      ? `<tr class="br-gruppe"><td colspan="${r.spalten.length}">${e(z[0] ?? '')}</td></tr>`
      : `<tr class="${zeilenArten?.[n] ? `br-${zeilenArten[n]}` : ''}">${z.map((x, i) => `<td class="${i === 0 || r.spalten[i] === 'Schüler*in' ? 'fw-semibold' : ''} ${zeilenArten && zahlSpalte[i] ? 'text-end' : ''}">${e(x ?? '')}</td>`).join('')}</tr>`).join('')}</tbody></table></div>`;
  if (art === 'entschuldigungsschreiben') {
    // je Schüler*in eine Seite mit Unterschriftsfeld
    const proSchueler = new Map();
    r.zeilen.forEach(z => proSchueler.set(z[0], [...(proSchueler.get(z[0]) ?? []), z]));
    vorschau.innerHTML = `<div class="br-vorschau s-karte">${vkopf(`${proSchueler.size} Schüler*innen`)}
      ${proSchueler.size ? [...proSchueler].map(([name, zeilen]) => `<div class="p-3 br-seite border-bottom">${kopf}<h3 class="h6 fw-bold mt-2">Entschuldigung für ${e(name)} (${e(zeilen[0][1])})</h3>
        <p class="small mb-2">Hiermit entschuldige ich das Fehlen meines Kindes zu folgenden Zeiten:</p>
        <table class="table table-sm mb-3"><thead><tr><th>Datum</th><th>Stunden</th><th>Fächer</th><th>Grund</th></tr></thead><tbody>${zeilen.map(z => `<tr><td>${e(z[2])}</td><td>${e(z[3])}</td><td>${e(z[4])}</td><td>${e(z[5])}</td></tr>`).join('')}</tbody></table>
        <div class="d-flex gap-4 small pt-4"><span class="border-top pt-1 flex-fill">Datum</span><span class="border-top pt-1 flex-fill">Unterschrift Erziehungsberechtigte</span></div></div>`).join('')
        : leer('Keine offenen Fehlstunden im Zeitraum.', 'check2-circle')}</div>`;
  } else {
    const datenzeilen = zeilenArten ? zeilenArten.filter(x => !x).length : r.zeilen.length;
    vorschau.innerHTML = `<div class="br-vorschau s-karte">${kopf}${vkopf(`${datenzeilen} ${datenzeilen === 1 ? 'Zeile' : 'Zeilen'}`)}
      ${r.hinweis ? `<div class="br-vhinweis"><i class="bi bi-info-circle"></i> ${e(r.hinweis)}</div>` : ''}
      ${r.zeilen.length ? tabelle(r.zeilen) : leer('Keine Daten für diese Auswahl.', 'file-earmark')}</div>`;
    vorschau.querySelector('[data-v-pdf]').onclick = ev => exportieren(a, p, 'pdf', ev.currentTarget);
    vorschau.querySelector('[data-v-csv]').onclick = ev => exportieren(a, p, 'csv', ev.currentTarget);
  }
  // Druck im Hoch- bzw. Querformat (Entschuldigungsschreiben als Brief hoch, Tabellen quer)
  const druck = () => druckenMitAusrichtung(art === 'entschuldigungsschreiben' ? 'hoch' : 'quer', art === 'entschuldigungsschreiben' ? 'entschuldigungsschreiben' : 'bericht-druck');
  vorschau.querySelector('[data-v-drucken]').onclick = druck;
  if (sessionStorage.getItem('untplan:berichtdruck')) { sessionStorage.removeItem('untplan:berichtdruck'); setTimeout(druck, 300); }
}
