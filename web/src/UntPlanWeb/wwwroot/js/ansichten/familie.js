// Seiten für Schüler*innen und Erziehungsberechtigte im neuen Design, aufgebaut wie im Original (die Übersicht steht für alle
// Rollen in uebersicht.js und nutzt die Kinderwahl von hier): Abwesenheiten mit den Reitern
// „Meine Abwesenheiten“ (Zeiträume) und „Fehlzeiten“ (je Unterrichtsstunde), Hausaufgaben (Datum + Woche/Monat).
// Erziehungsberechtigte mit mehreren Kindern wählen das Kind in der Filterleiste. Alle Daten kommen aus /api/app/familie/…,
// der Server prüft Rolle und Zuordnung.
import { e, holen, heuteIso, plusTage, toast, pdfAusrichtung } from '../ui.js';

const WT = ['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'];
const STATUS = { offen: ['gelb', 'offen'], entschuldigt: ['gruen', 'entschuldigt'], unentschuldigt: ['rot', 'nicht entschuldigt'] };
const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';
const wt = iso => WT[new Date(iso + 'T12:00').getDay()];
const tagMonat = iso => `${iso.slice(8, 10)}.${iso.slice(5, 7)}.`;
const monatPlus = (iso, n) => { const d = new Date(iso + 'T12:00'); d.setMonth(d.getMonth() + n); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`; };

/** Status-Label wie im Original (klein, gefüllt) – ändert beim Überfahren nie die Farbe. */
export const statusLabel = st => { const [farbe, text] = STATUS[st] ?? ['grau', st || '']; return `<span class="fz-label ${farbe}">${e(text)}</span>`; };
/** „zählt“ als farbiges Kästchen. */
export const zaehltKaestchen = z => `<span class="fz-kaestchen ${z ? 'an' : ''}" title="${z ? 'zählt als Fehlstunde' : 'zählt nicht als Fehlstunde'}" role="img" aria-label="${z ? 'zählt' : 'zählt nicht'}"></span>`;
/** Datum wie im Original: grauer Wochentag, dunkles Datum, graue Uhrzeit. */
const datumZelle = (iso, zeit, vor = '') => `<span class="fz-grau">${vor}${wt(iso)}</span> <span class="fz-datum">${tagMonat(iso)}</span>${zeit ? ` <span class="fz-grau">${e(zeit)}</span>` : ''}`;


/** Fehlzeitentabelle (Bild „Fehlzeiten“): je Unterrichtsstunde eine Zeile. Wird auch im Klassenbuch (Fehlzeiten) verwendet. */
export function fehlzeitenTabelle(zeilen, grundName = g => g, { schueler = false } = {}) {
  if (!zeilen.length) return '<div class="fz-leer">Keine Fehlzeiten im Zeitraum.</div>';
  return `<table class="fz-tabelle-neu"><thead><tr>${schueler ? '<th>Schüler*in</th>' : ''}<th>Datum</th><th>Zeit</th><th>Fach</th><th>Lehrkraft</th><th class="zahl">Fehltage</th><th class="zahl">Fehlstd.</th>
      <th class="zahl">Fehlmin.</th><th class="mitte">zählt</th><th>Abwesenheitsgrund</th><th>Status</th><th>Text</th></tr></thead><tbody>
    ${zeilen.map(z => `<tr>${schueler ? `<td>${e(z.schueler)}</td>` : ''}<td class="text-nowrap">${datumZelle(z.datum)}</td>
      <td class="text-nowrap fz-grau" title="${z.abwesendVon !== z.beginn || z.abwesendBis !== z.ende ? `abwesend ${e(z.abwesendVon)} – ${e(z.abwesendBis)}` : ''}">${e(z.beginn)} - ${e(z.ende)}${z.abwesendVon !== z.beginn || z.abwesendBis !== z.ende ? `<div class="fz-klein">abwesend ${e(z.abwesendVon)}–${e(z.abwesendBis)}</div>` : ''}</td>
      <td>${e(z.fach)}</td><td>${e(z.lehrer)}</td><td class="zahl">${z.fehltage}</td><td class="zahl">${z.fehlstunden}</td><td class="zahl">${z.fehlminuten}</td>
      <td class="mitte">${zaehltKaestchen(z.zaehlt)}</td><td>${e(grundName(z.grund))}</td><td>${statusLabel(z.status)}</td><td class="fz-text">${e(z.text)}</td></tr>`).join('')}
    </tbody></table>`;
}

/** Kinder des Kontos und das gewählte Kind (Erziehungsberechtigte mit mehreren Kindern). */
export async function kinder() {
  const liste = await holen('/api/app/familie/kinder').catch(() => []);
  let id = null;
  try { id = Number(sessionStorage.getItem('untplan:kind')) || null; } catch { /* privat */ }
  const kind = liste.find(k => k.id === id) ?? liste[0] ?? null;
  return { liste, kind };
}

export function kindWahl(liste, kind) {
  if (liste.length < 2) return '';
  return `<label class="fm-wahl"><span class="visually-hidden">Kind</span><select data-kind>${liste.map(k => `<option value="${k.id}" ${k.id === kind.id ? 'selected' : ''}>${e(k.vorname)} ${e(k.name)} (${e(k.klasse)})</option>`).join('')}</select></label>`;
}

export function kindBinden(wurzel, neu) {
  wurzel.querySelector('[data-kind]')?.addEventListener('change', ev => {
    try { sessionStorage.setItem('untplan:kind', ev.target.value); } catch { /* privat */ }
    neu();
  });
}

const ohneZuordnung = seite => { seite.innerHTML = '<div class="fm-karte"><div class="fm-leer">Ihrem Konto ist noch keine Schülerin bzw. kein Schüler zugeordnet. Bitte wenden Sie sich an die Schule.</div></div>'; };

// ------------------------------------------------------------------ Abwesenheiten (Reiter „Meine Abwesenheiten“ | „Fehlzeiten“)

/** Status einer Abwesenheit: bei einzeln (z. B. von Fachlehrkräften) entschuldigten Stunden „teilweise“ mit Anzahl. */
const zeitraumStatus = z => z.statusGemischt
  ? `<span class="fz-label gelb" title="${z.entschuldigt} entschuldigt, ${z.unentschuldigt} nicht entschuldigt, ${z.fehlstunden - z.entschuldigt - z.unentschuldigt} offen">teilweise</span><div class="fz-klein">${z.entschuldigt}/${z.fehlstunden} entschuldigt</div>`
  : statusLabel(z.status);

/** Stunden einer Abwesenheit (Details beim Aufklappen): Datum, Zeit, Fach, Lehrkraft, Status. */
const stundenDetails = (stunden, spalten) => `<tr class="fm-details"><td></td><td colspan="${spalten - 1}">${stunden.length
  ? `<table class="fm-stunden"><tbody>${stunden.map(f => `<tr><td class="text-nowrap">${datumZelle(f.datum)}</td><td class="text-nowrap fz-grau">${e(f.stunde)}. Std. ${e(f.abwesendVon)}–${e(f.abwesendBis)}</td>
      <td>${e(f.fach)}</td><td>${e(f.lehrer)}</td><td>${f.zaehlt ? `${f.fehlminuten} min` : `<span class="fz-grau">${f.art === 'verspaetet' ? 'verspätet' : 'vorzeitig'} ${f.fehlminuten} min</span>`}</td><td>${statusLabel(f.status)}</td></tr>`).join('')}</tbody></table>`
  : '<span class="fz-grau">Keine Unterrichtsstunden laut Stundenplan.</span>'}</td></tr>`;

export async function abwesenheiten(seite, kontext, [reiterParam, vonParam, statusParam, bisParam]) {
  kontext.titel('');
  const { liste, kind } = await kinder();
  if (!kind) return ohneZuordnung(seite);
  const reiter = reiterParam === 'fehlzeiten' ? 'fehlzeiten' : 'meine';
  const sj = kontext.schuljahr;
  const start = sj?.von && sj.von <= heuteIso() ? sj.von : plusTage(heuteIso(), -90);
  const von = /^\d{4}-\d{2}-\d{2}$/.test(vonParam ?? '') ? vonParam : start;
  // Ende: gewählt (Filter „bis“) bzw. Ende des Schuljahres – so erscheinen auch schon eingetragene künftige Abwesenheiten
  const bisWahl = /^\d{4}-\d{2}-\d{2}$/.test(bisParam ?? '') && bisParam >= von ? bisParam : '';
  const bis = bisWahl || (sj?.bis && sj.bis > von ? sj.bis : plusTage(von, 365));
  const status = ['offen', 'entschuldigt', 'unentschuldigt'].includes(statusParam) ? statusParam : '';
  const ziel = (r, v = von, s = status, b = bisWahl) => `#/abwesenheiten/${r}/${v}/${s || '-'}${b ? `/${b}` : ''}`;
  const q = `schueler=${kind.id}&von=${von}&bis=${bis}${status ? `&status=${status}` : ''}`;
  // „Meine Abwesenheiten“ lädt die Fehlzeiten (je Stunde) mit, damit jede Abwesenheit ihre Stunden aufklappen kann
  const [daten, gruende, stundenDaten] = await Promise.all([holen(`/api/app/familie/${reiter === 'meine' ? 'abwesenheiten' : 'fehlzeiten'}?${q}`), holen('/api/app/familie/gruende').catch(() => ({})),
    reiter === 'meine' ? holen(`/api/app/familie/fehlzeiten?schueler=${kind.id}&von=${von}&bis=${bis}`).catch(() => null) : null]);
  const stundenVon = new Map((stundenDaten?.zeilen ?? []).map(f => [f.id, f]));
  const grundName = g => gruende[g] ?? g;
  const zeilen = daten.zeilen;
  const export_ = (format, sym, titel) => `<a class="fm-export" href="/api/app/familie/export?liste=${reiter === 'meine' ? 'abwesenheiten' : 'fehlzeiten'}&format=${format}&${q}" download title="${titel}" aria-label="${titel}"><i class="bi ${sym}"></i></a>`;
  const tabelle = reiter === 'meine'
    ? (zeilen.length ? `<table class="fz-tabelle-neu fm-abw"><thead><tr><th class="nl-wahl"><input type="checkbox" class="fm-check" data-alle aria-label="Alle auswählen"></th><th><i class="bi bi-caret-down-fill fz-sort"></i>Von</th><th>Bis</th><th>Abwesenheitsgrund</th><th>Status</th><th class="zahl">Fehlstd.</th><th>Text</th><th></th></tr></thead><tbody>
        ${zeilen.map((z, i) => `<tr class="fm-zeile" data-zeile="${i}" tabindex="0" aria-expanded="false" title="Stunden anzeigen"><td class="nl-wahl"><input type="checkbox" class="fm-check" data-i="${i}" aria-label="Abwesenheit auswählen"></td>
          <td class="text-nowrap">${datumZelle(z.vonDatum, z.vonZeit)}</td><td class="text-nowrap">${datumZelle(z.bisDatum, z.bisZeit, 'bis ')}</td>
          <td>${e(grundName(z.grund))}${z.art !== 'fehlt' ? ` <span class="fz-grau">(${z.art === 'verspaetet' ? 'verspätet' : 'vorzeitig gegangen'} ${z.minuten} min)</span>` : ''}</td>
          <td>${zeitraumStatus(z)}</td><td class="zahl">${z.fehlstunden}</td><td class="fz-text">${e(z.text)}</td><td class="fm-auf"><i class="bi bi-chevron-down" aria-hidden="true"></i></td></tr>`).join('')}</tbody></table>`
      : '<div class="fz-leer">Keine Abwesenheiten im Zeitraum.</div>')
    : fehlzeitenTabelle(zeilen, grundName);
  seite.innerHTML = `
    <nav class="u-reiter fm-reiter"><a href="${ziel('meine')}" class="${reiter === 'meine' ? 'aktiv' : ''}">Meine Abwesenheiten</a><a href="${ziel('fehlzeiten')}" class="${reiter === 'fehlzeiten' ? 'aktiv' : ''}">Fehlzeiten</a></nav>
    <div class="fm-karte fm-filter">
      <button type="button" class="fm-pfeiltaste" data-monat="-1" title="Einen Monat früher" aria-label="Einen Monat früher"><i class="bi bi-arrow-left"></i></button>
      <label class="fm-datum"><span class="visually-hidden">Ab Datum</span><input type="date" data-von value="${von}"></label>
      <button type="button" class="fm-pfeiltaste" data-monat="1" title="Einen Monat später" aria-label="Einen Monat später"><i class="bi bi-arrow-right"></i></button>
      <label class="fm-datum fm-bis"><span class="fz-grau small">bis</span><span class="visually-hidden">Bis Datum</span><input type="date" data-bis value="${bis}" min="${von}"></label>
      ${kindWahl(liste, kind)}
      <label class="fm-wahl"><span class="visually-hidden">Status</span><select data-status><option value="">- Alle -</option>${Object.entries(STATUS).map(([k, [, t]]) => `<option value="${k}" ${k === status ? 'selected' : ''}>${t}</option>`).join('')}</select></label>
      <span class="ms-auto d-flex gap-1">${export_('pdf', 'bi-file-earmark-pdf', 'Als PDF exportieren')}${export_('csv', 'bi-file-earmark-excel', 'Für Excel exportieren')}${export_('csv', 'bi-filetype-csv', 'Als CSV exportieren')}</span>
    </div>
    <div class="fm-karte fm-tabelle">${tabelle}<div class="fm-fuss">${zeilen.length} ${zeilen.length === 1 ? 'Element' : 'Elemente'}</div></div>
    ${reiter === 'meine' ? `<div class="fm-karte fm-aktionen"><button type="button" class="fm-pille" data-brief>Entschuldigungsschreiben drucken <i class="bi bi-caret-up-fill"></i></button>
      <span class="fz-grau small">Ohne Auswahl: alle noch nicht entschuldigten Abwesenheiten im Zeitraum.</span></div>` : ''}`;
  const neu = (v = von, s = status, b = bisWahl) => kontext.navigieren(ziel(reiter, v, s, b && b >= v ? b : ''));
  seite.querySelector('[data-von]').onchange = ev => ev.target.value && neu(ev.target.value);
  seite.querySelector('[data-bis]').onchange = ev => neu(von, status, ev.target.value);
  // Details: Klick auf eine Abwesenheit klappt ihre Stunden auf (nicht beim Ankreuzen)
  seite.querySelectorAll('[data-zeile]').forEach(tr => {
    const umschalten = () => {
      const offen = tr.nextElementSibling?.classList.contains('fm-details');
      if (offen) tr.nextElementSibling.remove();
      else tr.insertAdjacentHTML('afterend', stundenDetails(zeilen[Number(tr.dataset.zeile)].ids.map(id => stundenVon.get(id)).filter(Boolean), tr.children.length));
      tr.setAttribute('aria-expanded', String(!offen));
      tr.classList.toggle('auf', !offen);
    };
    tr.addEventListener('click', ev => { if (!ev.target.closest('input')) umschalten(); });
    tr.addEventListener('keydown', ev => { if ((ev.key === 'Enter' || ev.key === ' ') && !ev.target.closest('input')) { ev.preventDefault(); umschalten(); } });
  });
  seite.querySelectorAll('[data-monat]').forEach(b => b.onclick = () => neu(monatPlus(von, Number(b.dataset.monat))));
  seite.querySelector('[data-status]').onchange = ev => neu(von, ev.target.value);
  kindBinden(seite, () => kontext.navigieren(location.hash));
  seite.querySelector('[data-alle]')?.addEventListener('change', ev => seite.querySelectorAll('[data-i]').forEach(c => { c.checked = ev.target.checked; }));
  seite.querySelector('[data-brief]')?.addEventListener('click', async () => {
    const ids = [...seite.querySelectorAll('[data-i]:checked')].map(c => zeilen[Number(c.dataset.i)].ids[0]);
    const ausrichtung = await pdfAusrichtung('hoch', 'entschuldigung');
    if (!ausrichtung) return;
    const url = `/api/app/familie/entschuldigung?schueler=${kind.id}&von=${von}&bis=${bis}${ids.length ? `&ids=${ids.join(',')}` : ''}&ausrichtung=${ausrichtung}`;
    // Fehler (z. B. nichts zu entschuldigen) als Meldung statt als leere Seite
    const antwort = await fetch(url, { credentials: 'same-origin' });
    if (!antwort.ok) { toast((await antwort.json().catch(() => null))?.fehler ?? 'Das Entschuldigungsschreiben konnte nicht erstellt werden.', true); return; }
    const blob = await antwort.blob();
    const a = Object.assign(document.createElement('a'), { href: URL.createObjectURL(blob), download: `entschuldigung-${kind.name.toLowerCase()}.pdf` });
    document.body.append(a); a.click(); setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 1000);
  });
}

// ------------------------------------------------------------------ Hausaufgaben (Datum + Woche/Monat)

export async function hausaufgaben(seite, kontext, [vonParam, zeitraumParam]) {
  kontext.titel('');
  const { liste, kind } = await kinder();
  if (!kind) return ohneZuordnung(seite);
  const zeitraum = zeitraumParam === 'woche' ? 'woche' : 'monat';
  const heute = heuteIso();
  const von = /^\d{4}-\d{2}-\d{2}$/.test(vonParam ?? '') ? vonParam : zeitraum === 'monat' ? `${heute.slice(0, 8)}01` : plusTage(heute, -((new Date(heute + 'T12:00').getDay() + 6) % 7));
  const bis = zeitraum === 'monat' ? plusTage(monatPlus(von, 1), -1) : plusTage(von, 6);
  const d = await holen(`/api/app/familie/hausaufgaben?schueler=${kind.id}&von=${von}&bis=${bis}`);
  const zeilen = d.zeilen;
  const ziel = (v, z = zeitraum) => `#/hausaufgaben/${v}/${z}`;
  seite.innerHTML = `<h1 class="fm-titel">Hausaufgaben</h1>
    <div class="fm-karte fm-filter">
      <button type="button" class="fm-pfeiltaste" data-schritt="-1" title="Zurück" aria-label="Zurück"><i class="bi bi-arrow-left"></i></button>
      <label class="fm-datum"><span class="visually-hidden">Datum</span><input type="date" data-von value="${von}"></label>
      <button type="button" class="fm-pfeiltaste" data-schritt="1" title="Weiter" aria-label="Weiter"><i class="bi bi-arrow-right"></i></button>
      <label class="fm-wahl"><span class="visually-hidden">Zeitraum</span><select data-zeitraum><option value="woche" ${zeitraum === 'woche' ? 'selected' : ''}>Woche</option><option value="monat" ${zeitraum === 'monat' ? 'selected' : ''}>Monat</option></select></label>
      ${kindWahl(liste, kind)}
    </div>
    <div class="fm-karte fm-tabelle">${zeilen.length ? `<table class="fz-tabelle-neu"><thead><tr><th>Fächer</th><th>Lehrkräfte</th><th>Aufgabedatum</th><th><i class="bi bi-caret-up-fill fz-sort"></i>Fälligkeitsdatum</th><th>Text</th></tr></thead><tbody>
      ${zeilen.map(h => `<tr class="${h.erledigt ? 'fz-erledigt' : ''}"><td>${e(h.fach)}</td><td>${e(h.lehrer)}</td><td class="text-nowrap">${datumZelle(h.datum)}</td>
        <td class="text-nowrap">${h.faellig ? datumZelle(h.faellig) : ''}</td><td class="fz-text breit">${e(h.text)}${h.bemerkung ? `<div class="fz-klein">${e(h.bemerkung)}</div>` : ''}</td></tr>`).join('')}</tbody></table>`
      : '<table class="fz-tabelle-neu"><thead><tr><th>Fächer</th><th>Lehrkräfte</th><th>Aufgabedatum</th><th><i class="bi bi-caret-up-fill fz-sort"></i>Fälligkeitsdatum</th><th>Text</th></tr></thead></table>'}
      <div class="fm-fuss">${zeilen.length} ${zeilen.length === 1 ? 'Element' : 'Elemente'}</div></div>`;
  seite.querySelector('[data-von]').onchange = ev => ev.target.value && kontext.navigieren(ziel(ev.target.value));
  seite.querySelector('[data-zeitraum]').onchange = ev => kontext.navigieren(ziel(von, ev.target.value));
  seite.querySelectorAll('[data-schritt]').forEach(b => b.onclick = () => kontext.navigieren(ziel(zeitraum === 'monat' ? monatPlus(von, Number(b.dataset.schritt)) : plusTage(von, 7 * Number(b.dataset.schritt)))));
  kindBinden(seite, () => kontext.navigieren(location.hash));
}
