// Klassenbuch → Berichte im nl-design: Filterleiste (Klasse, Schüler*in, Zeitraum), Reiter je Berichtsgruppe, Berichte als ruhige Liste
// (Symbol, Titel, Beschreibung, rechts PDF/CSV); Parameter im Seitenpanel. Keine Vorschau – Berichte nur als PDF bzw. CSV.
import { e, icon, holen, leer, heuteIso, plusTage, herunterladen, ApiFehler, pdfAusrichtung, suchfeldMitFilter, suchfeldMitFilterBinden } from '../ui.js';
import { kbDialog as dialog } from '../kbdialog.js';
import { zeitbereichFeld, zeitbereichBinden } from '../zeitbereich.js';

/** Berichte, die es im Original nur als PDF gibt. */
const NUR_PDF = ['entschuldigungsschreiben', 'klassenbuch_deckblatt'];
const ALIAS = { fehlzeiten: 'fehlzeiten_schueler', lehrstoff: 'arbeitsbericht_tag' };
const GRUPPEN = {
  Abwesenheiten: { symbol: 'person-x', farbe: 'rot' },
  Klassenbucheinträge: { symbol: 'journal-text', farbe: 'blau' },
  Arbeitsbericht: { symbol: 'journal-check', farbe: 'gruen' },
  Prüfungen: { symbol: 'file-earmark-check', farbe: 'gelb' },
  Weitere: { symbol: 'grid', farbe: 'lila' },
  Klassenbuch: { symbol: 'journal-bookmark', farbe: 'blau' },
};
/** Einstellungen je Bericht (Auswahl wie „pro Stunde“, Haken wie „Fehltage und -zeiten trennen“) – je Sitzung gemerkt. */
const OPT_SCHLUESSEL = 'untplan:berichtoptionen';
let optStand = {};
try { optStand = JSON.parse(sessionStorage.getItem(OPT_SCHLUESSEL) || '{}') || {}; } catch { optStand = {}; }
const optWahl = (a, o) => optStand[a.id]?.[o.name] ?? o.vorgabe;
const optQuery = (a, extra = {}) => (a.optionen ?? []).map(o => `&${o.name}=${encodeURIComponent(extra[o.name] ?? optWahl(a, o))}`).join('');
const optMerken = (a, werte) => { optStand[a.id] = { ...(optStand[a.id] ?? {}), ...werte }; try { sessionStorage.setItem(OPT_SCHLUESSEL, JSON.stringify(optStand)); } catch { /* privat */ } };
/** Kurzfassung der gewählten Auswahl-Einstellungen für die Berichtszeile (z. B. „pro Stunde“). */
const optKurz = a => (a.optionen ?? []).filter(o => o.typ === 'auswahl').map(o => o.werte.find(w => w.wert === optWahl(a, o))?.text).filter(t => t && t !== '- Alle -');
/** Schuljahresende: die 6 Dateien des Klassenbuchs mit Bericht, Einstellungen und Dateiname. */
const ABSCHLUSS = [
  { nr: '01', name: 'Deckblatt', art: 'klassenbuch_deckblatt', titel: 'Klassenbuch Deckblatt', opt: {} },
  { nr: '02', name: 'Lehrnachweis', art: 'wochenuebersicht', titel: 'Wochenübersicht Klasse mit Abwesenheiten', opt: { mitabw: '1' } },
  { nr: '03', name: 'Fehlzeitenstatistik', art: 'fehlzeiten_klasse', titel: 'Fehlzeiten pro Klasse – Gesamtübersicht', opt: { ausgabe: 'gesamt', status: 'alle', fehlzeiten: '1', verspaetungen: '1', zaehlend: '1', alle: '1', sortiert: '1', trennen: '1' } },
  { nr: '04', name: 'Fehlzeiten-e', art: 'fehlzeiten_klasse', titel: 'Fehlzeiten pro Klasse – pro Stunde [entschuldigt]', opt: { ausgabe: 'pro_stunde', status: 'entschuldigt', sortiert: '1', trennen: '1' } },
  { nr: '05', name: 'Fehlzeiten-u', art: 'fehlzeiten_klasse', titel: 'Fehlzeiten pro Klasse – pro Stunde [nicht entschuldigt]', opt: { ausgabe: 'pro_stunde', status: 'nicht_entschuldigt', sortiert: '1', trennen: '1' } },
  { nr: '06', name: 'Klassenbucheinträge', art: 'klassenbucheintraege', titel: 'Klassenbucheinträge pro Klasse sortiert nach Schülernamen', opt: { sortiert: '1' } },
];
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
  // Berichte zum Unterricht stehen unter Unterricht → Mein Unterricht → Berichte
  const arten = (await holen('/api/app/berichte/liste')).filter(a => !a.unterricht);
  const art = arten.find(a => a.id === (ALIAS[berichtParam] ?? berichtParam))?.id ?? null;
  const iso = /^\d{4}-\d{2}-\d{2}$/;
  // Vorbelegung wie im Original: Zeitraum „Aktuelles Schuljahr“, Klasse die eigene Klasse (Klassenleitung)
  const sjK = kontext.sitzung?.schuljahr ?? kontext.schuljahr ?? {};
  const von = iso.test(vonParam ?? '') ? vonParam : sjK.von || plusTage(heuteIso(), -30);
  const bis = iso.test(bisParam ?? '') ? bisParam : sjK.bis || heuteIso();
  const eigeneKlasse = el.klassen.find(k => kontext.ich?.element && (k.leitung ?? []).some(l => l.toLowerCase() === kontext.ich.element.toLowerCase()))?.kurz ?? '';
  const klasse = klasseParam && klasseParam !== '-' ? klasseParam : klasseParam === '-' ? '' : eigeneKlasse;
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
  const vorgabe = (a, n) => n === 1 ? (a.id === art && n1 ? n1 : a.id === 'unentschuldigt' ? 3 : 16) : (a.id === art && n2 ? n2 : 4);
  const abfrage = (p) => `klasse=${encodeURIComponent(p.klasse)}&von=${p.von}&bis=${p.bis}${p.schueler ? `&schueler=${p.schueler}` : ''}&n1=${p.n1}&n2=${p.n2}`;
  const standard = a => ({ klasse, schueler, von, bis, n1: vorgabe(a, 1), n2: vorgabe(a, 2) });

  kontext.titel('Berichte', '', klasse ? `Klasse ${klasse}` : 'alle Klassen');
  seite.innerHTML = `<div class="br-seite">
    <!-- weißer Kopf (app.js): Suche und Parameter der Berichte oben, Vorschau und Liste darunter auf grauem Grund -->
    <div class="u-kopfteil br-kopf nicht-drucken">
      <div class="u-sf-leiste" data-gruppen>${suchfeldMitFilter({ platzhalter: 'Bericht suchen', label: 'Bericht suchen', filter: [
        { name: 'gruppe', label: 'Berichtsgruppe', alle: `Alle Berichte (${arten.length})`, standard: 'alle', wert: gruppe, optionen: gruppen.map(g => [g, `${g} (${arten.filter(a => a.gruppe === g).length})`]) },
      ] })}</div>
      <div class="kd-filter br-filter nicht-drucken">
        <label class="sg-feld"><span>Klasse</span><select class="form-select" data-klasse><option value="">- Alle -</option>${el.klassen.map(k => `<option ${k.kurz === klasse ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select></label>
        <label class="sg-feld"><span>Schüler*innen</span><select class="form-select" data-schueler ${klasse ? '' : 'disabled'}><option value="">- Alle -</option>${schuelerListe.map(s => `<option value="${s.id}" ${String(s.id) === schueler ? 'selected' : ''}>${e(s.name)} ${e(s.vorname)}</option>`).join('')}</select></label>
        ${zeitbereichFeld(von, bis, sjK)}
      </div>
    </div>
    <div class="br-raster nicht-drucken" data-raster></div>
    <p class="br-fussnote nicht-drucken"><i class="bi bi-info-circle"></i> Notfallliste, BAföG- und Fahrkostenbescheinigungen benötigen Adress- bzw. Kontaktdaten, die UntPlan nicht überträgt.</p>
  </div>`;

  const raster = seite.querySelector('[data-raster]');
  // Einstellungen wie im Original direkt in der Berichtszeile (z. B. „pro Stunde“, „- Alle -“, „ohne Seitenumbrüche“); die Haken Fehlzeiten,
  // Verspätungen, Nur zählende Abwesenheiten und Alle Schüler*innen stehen einmal im Kopf des Abschnitts „Abwesenheiten“
  const GEMEINSAM = ['fehlzeiten', 'verspaetungen', 'zaehlend', 'alle'];
  const zeilenOptionen = a => (a.optionen ?? []).filter(o => !GEMEINSAM.includes(o.name)).map(o => o.typ === 'haken'
    ? `<label class="br-zopt"><input type="checkbox" class="form-check-input" data-zopt="${e(a.id)}" data-name="${e(o.name)}" ${optWahl(a, o) === '1' ? 'checked' : ''}>${e(o.titel)}</label>`
    : `<select class="form-select form-select-sm br-zwahl" data-zopt="${e(a.id)}" data-name="${e(o.name)}" aria-label="${e(o.titel)}">${o.werte.map(w => `<option value="${e(w.wert)}" ${w.wert === optWahl(a, o) ? 'selected' : ''}>${e(w.text)}</option>`).join('')}</select>`).join('');
  const gemeinsamKopf = g => {
    const mit = arten.filter(a => a.gruppe === g && (a.optionen ?? []).some(o => GEMEINSAM.includes(o.name)));
    if (!mit.length) return '';
    return `<span class="br-gemeinsam">${GEMEINSAM.map(n => { const a = mit.find(x => x.optionen.some(o => o.name === n)); const o = a?.optionen.find(x => x.name === n);
      return o ? `<label class="br-zopt"><input type="checkbox" class="form-check-input" data-gopt="${e(n)}" data-gruppe="${e(g)}" ${optWahl(a, o) === '1' ? 'checked' : ''}>${e(o.titel)}</label>` : ''; }).join('')}</span>`;
  };
  // je Bericht eine ruhige Zeile: Symbol, Titel und Beschreibung (öffnet die Parameter), Hinweise, rechts die Aktionen
  const karte = a => {
    const g = GRUPPEN[a.gruppe] ?? GRUPPEN.Weitere;
    const fehltKlasse = a.klasseNoetig && !klasse;
    return `<div class="br-reihe ${a.id === art ? 'aktiv' : ''}" data-karte="${a.id}" role="listitem">
      <span class="br-symbol ${g.farbe}"><i class="bi bi-${g.symbol}" aria-hidden="true"></i></span>
      <button type="button" class="br-reihe-text" data-panel="${a.id}" aria-label="${e(a.titel)}: Parameter und Export">
        <b>${e(a.titel)}</b></button>
      <span class="br-reihe-tags">${a.klasseNoetig ? `<span class="s-label ${fehltKlasse ? 'orange' : ''}">${fehltKlasse ? 'Klasse wählen' : 'je Klasse'}</span>` : ''}
        ${a.parameter ? `<span class="s-label">${e(a.parameter.replace('{n1}', vorgabe(a, 1)).replace('{n2}', vorgabe(a, 2)))}</span>` : ''}
        ${zeilenOptionen(a)}</span>
      <span class="br-reihe-aktionen">
        <button type="button" class="br-knopf" data-pdf="${a.id}" ${fehltKlasse ? 'disabled' : ''} title="Als PDF herunterladen"><i class="bi bi-file-earmark-pdf"></i><span>PDF</span></button>
        <button type="button" class="br-knopf" data-csv="${a.id}" ${fehltKlasse || NUR_PDF.includes(a.id) ? 'disabled' : ''} title="Als CSV herunterladen"><i class="bi bi-filetype-csv"></i><span>CSV</span></button>
      </span></div>`;
  };
  const rasterZeichnen = () => {
    const q = suche.trim().toLowerCase();
    const sicht = arten.filter(a => (gruppe === 'alle' || a.gruppe === gruppe) && (!q || `${a.titel} ${BESCHREIBUNG[a.id] ?? ''}`.toLowerCase().includes(q)));
    raster.innerHTML = sicht.length ? (gruppe === 'alle' && !q
      ? gruppen.map(g => `<section class="br-abschnitt"><h2>${e(g)}${gemeinsamKopf(g)}</h2><div class="br-liste" role="list">${arten.filter(a => a.gruppe === g).map(karte).join('')}${g === 'Klassenbuch' ? abschlussZeile() : ''}</div></section>`).join('')
      : `<div class="br-liste" role="list">${sicht.map(karte).join('')}</div>`)
      : `<div class="s-leer"><span class="s-leer-symbol"><i class="bi bi-search"></i></span><b>Kein Bericht gefunden</b><p>Für „${e(suche)}“ gibt es keinen passenden Bericht.</p></div>`;
    raster.querySelectorAll('[data-panel]').forEach(b => b.onclick = () => panel(arten.find(a => a.id === b.dataset.panel)));
    raster.querySelectorAll('[data-pdf]').forEach(b => b.onclick = () => { const a = arten.find(x => x.id === b.dataset.pdf); exportieren(a, standard(a), 'pdf', b); });
    raster.querySelectorAll('[data-csv]').forEach(b => b.onclick = () => { const a = arten.find(x => x.id === b.dataset.csv); exportieren(a, standard(a), 'csv', b); });
    raster.querySelectorAll('[data-abschluss]').forEach(b => b.onclick = abschluss);
    raster.querySelectorAll('[data-zopt]').forEach(x => x.onchange = () => optMerken(arten.find(a => a.id === x.dataset.zopt), { [x.dataset.name]: x.type === 'checkbox' ? (x.checked ? '1' : '0') : x.value }));
    raster.querySelectorAll('[data-gopt]').forEach(x => x.onchange = () => arten.filter(a => a.gruppe === x.dataset.gruppe && (a.optionen ?? []).some(o => o.name === x.dataset.gopt))
      .forEach(a => optMerken(a, { [x.dataset.gopt]: x.checked ? '1' : '0' })));
  };
  // Schuljahresende: Klassenbuch als PDF-Dateien (6 Dateien je Klasse bzw. Klassengruppe) – Zeitraum aktuelles Schuljahr bzw. bis zum letzten Schultag der Klasse
  function abschlussZeile() {
    return `<div class="br-reihe" role="listitem"><span class="br-symbol blau"><i class="bi bi-file-earmark-zip" aria-hidden="true"></i></span>
      <button type="button" class="br-reihe-text" data-abschluss><b>Schuljahresende: Klassenbuch als PDF-Datei</b></button>
      <span class="br-reihe-tags"><span class="s-label ${klasse ? '' : 'orange'}">${klasse ? 'je Klasse' : 'Klasse wählen'}</span></span>
      <span class="br-reihe-aktionen"><button type="button" class="br-knopf" data-abschluss ${klasse ? '' : 'disabled'}><i class="bi bi-files"></i><span>Dateien</span></button></span></div>`;
  }
  function abschluss() {
    if (!klasse) { kontext.fehler(new Error('Bitte zuerst im Kopf die Klasse einstellen.')); return; }
    document.querySelector('.br-dialog')?.closest('.kbd-overlay')?.remove();
    const sj = kontext.schuljahr ?? {};
    const h = dialog({ titel: 'Klassenbuch als PDF-Datei', breite: 'm', klasse: 'br-dialog',
      inhalt: `<div class="br-panel-rumpf">
        <label class="kd-dfeld"><span>Klasse</span><input class="form-control" value="${e(klasse)}" disabled></label>
        <div class="kd-dialog-reihe zwei">
          <label class="kd-dfeld"><span>Von</span><input type="date" class="form-control" data-a-von value="${e(sj.von || von)}"></label>
          <label class="kd-dfeld"><span>Bis</span><input type="date" class="form-control" data-a-bis value="${e(sj.bis && sj.bis < heuteIso() ? sj.bis : heuteIso())}"></label>
        </div>
        <ol class="br-abschluss-liste">${ABSCHLUSS.map((d, i) => `<li class="br-abschluss"><span class="flex-fill">${e(`${klasse}-${d.nr}-${d.name}.pdf`)}<br><small class="text-body-secondary">${e(d.titel)}</small></span>
          <button type="button" class="nl-knopf" data-datei="${i}"><i class="bi bi-file-earmark-pdf"></i>PDF</button></li>`).join('')}</ol>
      </div>`,
      fuss: `<button type="button" class="nl-knopf nw-primaer" data-alle-dateien><i class="bi bi-download"></i>Alle 6 Dateien herunterladen</button>` });
    const q = x => h.querySelector(x);
    const datei = async (d, knopf) => {
      const v = q('[data-a-von]').value || von, b = q('[data-a-bis]').value || bis;
      const ex = new URLSearchParams({ klasse, von: v, bis: b, format: 'pdf', ausrichtung: d.art === 'klassenbuch_deckblatt' ? 'hoch' : 'quer', ...d.opt });
      if (knopf) knopf.disabled = true;
      try { herunterladen(`${klasse}-${d.nr}-${d.name}.pdf`, await holeDatei(`/api/app/berichte/erstellen/${d.art}?${ex}`)); }
      catch (err) { kontext.fehler(err); }
      finally { if (knopf) knopf.disabled = false; }
    };
    h.querySelectorAll('[data-datei]').forEach(b => b.onclick = () => datei(ABSCHLUSS[Number(b.dataset.datei)], b));
    q('[data-alle-dateien]').onclick = async ev => { for (const d of ABSCHLUSS) await datei(d, ev.currentTarget); };
  }
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
  seite.querySelectorAll('.kd-filter [data-klasse], .kd-filter [data-schueler]').forEach(x => x.onchange = () =>
    kontext.navigieren(ziel(art, f('klasse') || '-', f('von') || von, f('bis') || bis, x.matches('[data-klasse]') ? '' : f('schueler'))));
  zeitbereichBinden(seite.querySelector('.br-filter'), sjK, (v, b) => kontext.navigieren(ziel(art, f('klasse') || '-', v, b, f('schueler'))));

  async function exportieren(a, p, format, knopf) {
    if (a.klasseNoetig && !p.klasse) { kontext.fehler(new Error('Für diesen Bericht bitte eine Klasse wählen.')); return; }
    // PDF: Hoch- oder Querformat wählen
    const ausrichtung = format === 'pdf' ? await pdfAusrichtung('quer', 'bericht') : null;
    if (format === 'pdf' && !ausrichtung) return;
    if (knopf) knopf.disabled = true;
    try { herunterladen(`${a.id}-${p.klasse || 'alle'}-${p.von}-${p.bis}.${format}`, await holeDatei(`/api/app/berichte/erstellen/${a.id}?${abfrage(p)}${optQuery(a)}&format=${format}${ausrichtung ? `&ausrichtung=${ausrichtung}` : ''}`)); }
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
          ${a.optionen?.length ? `<div class="kd-dfeld"><span>Einstellungen</span><div class="br-optionen">${a.optionen.map(o => o.typ === 'haken'
            ? `<label class="form-check"><input type="checkbox" class="form-check-input" data-opt="${e(o.name)}" ${optWahl(a, o) === '1' ? 'checked' : ''}><span class="form-check-label">${e(o.titel)}</span></label>`
            : `<select class="form-select form-select-sm" data-opt="${e(o.name)}" aria-label="${e(o.titel)}">${o.werte.map(w => `<option value="${e(w.wert)}" ${w.wert === optWahl(a, o) ? 'selected' : ''}>${e(w.text)}</option>`).join('')}</select>`).join('')}</div></div>` : ''}
        </div>`,
      fuss: `<button type="button" class="nl-knopf" data-p-csv ${NUR_PDF.includes(a.id) ? 'disabled' : ''}><i class="bi bi-filetype-csv"></i>CSV</button>
        <button type="button" class="nl-knopf nw-primaer" data-p-pdf><i class="bi bi-file-earmark-pdf"></i>PDF</button>` });
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
    const werte = () => (optMerken(a, Object.fromEntries([...h.querySelectorAll('[data-opt]')].map(x => [x.dataset.opt, x.type === 'checkbox' ? (x.checked ? '1' : '0') : x.value]))), {
      klasse: q('[data-p-klasse]').value, schueler: q('[data-p-schueler]').value, von: q('[data-p-von]').value || von, bis: q('[data-p-bis]').value || bis,
      n1: Number(q('[data-p-n1]')?.value) || p.n1, n2: Number(q('[data-p-n2]')?.value) || p.n2 });
    q('[data-p-pdf]').onclick = ev => exportieren(a, werte(), 'pdf', ev.currentTarget);
    q('[data-p-csv]').onclick = ev => exportieren(a, werte(), 'csv', ev.currentTarget);
    setTimeout(() => q('[data-p-klasse]').focus(), 50);
  }

  // Adresse mit Bericht (z. B. aus „Berichte ▾“ anderer Seiten): keine Vorschau, sondern Parameter und Export des Berichts
  if (art) panel(arten.find(x => x.id === art));
}
