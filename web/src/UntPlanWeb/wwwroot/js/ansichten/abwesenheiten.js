// Klassenbuch → Abwesenheiten wie im Original: Liste der Schülerabwesenheiten mit Filter (Klasse, Schüler*in, Zeitraum, Grund, Status),
// Neu/Bearbeiten/Löschen, Zusammenfassen und Entschuldigen durch die Klassenleitung, CSV; Reiter „Meine Abwesenheiten“ (eigene Meldungen).
import { e, icon, holen, senden, api, toast, leer, filterFeld, datumKachel, stundenText, heuteIso, plusTage, meldungText, statusMarker, herunterladen, infoHinweis, suchfeldMitFilter, suchfeldMitFilterBinden } from '../ui.js';
import { kbDialog as dialog, kbBestaetigen as bestaetigen } from '../kbdialog.js';
import { zeitbereichFeld, zeitbereichBinden } from '../zeitbereich.js';

const STATUS = { offen: '<span class="u-chip gelb">offen</span>', entschuldigt: '<span class="u-chip gruen">entschuldigt</span>', unentschuldigt: '<span class="u-chip rot">nicht entschuldigt</span>' };
const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';
/** Avatar mit Initialen (Nachname, Vorname) wie in der neuen Oberfläche; hervorgehoben = gefüllt. */
export const avatar = (name, gefuellt = true) => {
  const teile = String(name || '').replace(',', ' ').split(/\s+/).filter(Boolean);
  const ini = ((teile[0]?.[0] ?? '') + (teile[1]?.[0] ?? '')).toUpperCase();
  return `<span class="nl-avatar ${gefuellt ? 'voll' : ''}" aria-hidden="true">${e(ini)}</span>`;
};
const tagKurz = iso => new Date(iso + 'T12:00').toLocaleDateString('de-DE', { weekday: 'short', day: '2-digit', month: '2-digit' });

/** Gemeinsamer Zeitraum und Klasse von Fehlzeiten und Abwesenheiten (je Sitzung); mit Wert: speichern. */
export function abwZeitraum(wert) {
  try {
    if (wert) { sessionStorage.setItem('untplan:abw-zeitraum', JSON.stringify(wert)); return wert; }
    const x = JSON.parse(sessionStorage.getItem('untplan:abw-zeitraum') || 'null');
    return x && /^\d{4}-\d{2}-\d{2}$/.test(x.von) && /^\d{4}-\d{2}-\d{2}$/.test(x.bis) ? x : null;
  } catch { return null; }
}

/** Nachweisarten einer Entschuldigung. */
export const NACHWEISE = { attest: 'ärztliches Attest', schriftlich: 'schriftliche Entschuldigung', muendlich: 'mündlich', telefonisch: 'telefonisch', email: 'per E-Mail' };

/** Zusätzliche Angaben zu einer Abwesenheit (Nachweis, Eingang der Entschuldigung, Eltern informiert, interne Notiz). */
export const detailFelder = (z = {}) => `
  <div class="col-12"><div class="abw-details-titel">Weitere Angaben</div></div>
  <div class="col-sm-6"><label class="form-label">Nachweis</label><select class="form-select" name="nachweis"><option value="">kein Nachweis</option>${Object.entries(NACHWEISE).map(([k, t]) => `<option value="${k}" ${k === z.nachweis ? 'selected' : ''}>${t}</option>`).join('')}</select></div>
  <div class="col-sm-6"><label class="form-label">Entschuldigung eingegangen am</label><input type="date" class="form-control" name="entschuldigtAm" value="${e(z.entschuldigtAm || '')}"></div>
  <div class="col-12"><div class="form-check form-switch"><input class="form-check-input" type="checkbox" role="switch" id="abw-eltern" name="elternInformiert" ${z.elternInformiert ? 'checked' : ''}>
    <label class="form-check-label" for="abw-eltern">Eltern bzw. Erziehungsberechtigte informiert</label></div></div>
  <div class="col-12"><label class="form-label">Interne Notiz <span class="text-body-secondary small">(nur Klassenleitung)</span></label><textarea class="form-control" name="notizIntern" rows="2" maxlength="1000">${e(z.notizIntern || '')}</textarea></div>`;

/** Werte der Detailfelder aus einem Dialog. */
export const detailWerte = box => ({
  nachweis: box.querySelector('[name=nachweis]')?.value ?? '',
  entschuldigtAm: box.querySelector('[name=entschuldigtAm]')?.value ?? '',
  elternInformiert: !!box.querySelector('[name=elternInformiert]')?.checked,
  notizIntern: box.querySelector('[name=notizIntern]')?.value ?? '',
});

/** Kleine Symbole in der Liste: Nachweis, Eltern informiert, interne Notiz. */
const detailSymbole = z => [
  z.nachweis ? `<span class="nl-tag" title="Nachweis: ${e(NACHWEISE[z.nachweis] ?? z.nachweis)}${z.entschuldigtAm ? `, eingegangen am ${datumDe(z.entschuldigtAm)}` : ''}"><i class="bi bi-file-earmark-check me-1"></i>${e((NACHWEISE[z.nachweis] ?? z.nachweis).replace('ärztliches ', '').replace(' Entschuldigung', ''))}</span>` : '',
  z.elternInformiert ? '<span class="nl-tag" title="Eltern informiert"><i class="bi bi-telephone me-1"></i>Eltern</span>' : '',
  z.notizIntern ? `<span class="nl-tag" title="${e(z.notizIntern)}"><i class="bi bi-lock me-1"></i>Notiz</span>` : '',
].filter(Boolean).join(' ');

const ARTEN = { fehlt: 'abwesend', verspaetet: 'verspätet', vorzeitig: 'vorzeitig gegangen' };
/** Statusname zur Auswahl im Dialog „Entschuldigung“ (Grundstatus → Name der Statusliste). */
const STATUS_NAME = { offen: '[offen]', entschuldigt: 'Entschuldigt', unentschuldigt: 'Nicht entschuldigt' };
/** Status in der Liste wie im Original: blaues Fragezeichen für [offen], sonst „entsch.“, „nicht entsch.“ bzw. der Name (z. B. u-1Mahn) – Klick öffnet die Entschuldigung. */
const statusKnopf = (z, i) => {
  const name = z.statusName || '';
  const inhalt = z.status === 'offen' && !name ? '<span class="abw-frage" aria-hidden="true">?</span><span class="visually-hidden">offen</span>'
    : `<span class="u-chip ${z.status === 'entschuldigt' ? 'gruen' : z.status === 'offen' ? 'gelb' : 'rot'}">${e(name || (z.status === 'entschuldigt' ? 'entsch.' : 'nicht entsch.'))}</span>`;
  return `<button type="button" class="abw-status-knopf" data-entsch="${i}" title="Entschuldigung">${inhalt}</button>`;
};
const artChip = z => z.art === 'verspaetet' ? `<span class="u-chip gelb"><i class="bi bi-clock me-1"></i>verspätet ${z.minuten} min</span>`
  : z.art === 'vorzeitig' ? `<span class="u-chip lila"><i class="bi bi-box-arrow-right me-1"></i>vorzeitig ${z.minuten} min</span>`
  : /^(verspätet|vorzeitig gegangen)/.test(z.bemerkung || '') ? `abwesend <span class="small text-body-secondary">(${e(z.bemerkung.match(/^(verspätet|vorzeitig gegangen)/)[1])})</span>` : 'abwesend';

export async function zeigen(seite, kontext, [reiterParam, vonParam, bisParam, klasseParam, statusParam, grundParam, artParam, schuelerParam]) {
  // Schüler*innen und Erziehungsberechtigte: Reiter „Meine Abwesenheiten“ | „Fehlzeiten“
  // (Familien-URL: #/abwesenheiten/<reiter>/<von>/<status>/<bis>)
  if (kontext.familie) return (await import('./familie.js')).abwesenheiten(seite, kontext, [reiterParam, vonParam, bisParam, klasseParam]);
  const ich = kontext.ich;
  const el = await kontext.elemente();
  const reiter = reiterParam === 'meine' && ich.element ? 'meine' : 'schueler';
  const iso = /^\d{4}-\d{2}-\d{2}$/;
  // Zeitraum und Klasse gemeinsam mit den Fehlzeiten (dieselben Daten in beiden Bereichen)
  const gemeinsam = abwZeitraum();
  const von = iso.test(vonParam ?? '') ? vonParam : gemeinsam?.von ?? plusTage(heuteIso(), -14);
  const bis = iso.test(bisParam ?? '') ? bisParam : gemeinsam?.bis ?? heuteIso();
  const klasse = klasseParam && klasseParam !== '-' ? klasseParam : klasseParam === '-' ? '' : gemeinsam?.klasse ?? '';
  if (reiterParam !== 'meine') abwZeitraum({ von, bis, klasse: klasse || gemeinsam?.klasse || '' });
  const status = ['offen', 'entschuldigt', 'unentschuldigt'].includes(statusParam) ? statusParam : '';
  const grund = grundParam && grundParam !== '-' ? grundParam : '';
  const art = ARTEN[artParam] ? artParam : '';
  const schuelerWahl = klasse && /^\d+$/.test(schuelerParam ?? '') ? Number(schuelerParam) : null;
  const ziel = (r, v = von, b = bis, k = klasse, s = status, g = grund, a = art, sch = schuelerWahl) => `#/abwesenheiten/${r}/${v}/${b}/${encodeURIComponent(k || '-')}/${s || '-'}/${encodeURIComponent(g || '-')}${a || sch ? `/${a || '-'}` : ''}${sch ? `/${sch}` : ''}`;
  // weißer Kopf (app.js): Reiter und Suchleiste gehören zum Kopf; die Zeile der Suchleiste ist beim Laden schon frei (kein Springen)
  seite.innerHTML = `
    ${ich.element ? `<div class="u-kopfteil ${reiter === 'schueler' ? 'u-kopfreiter' : ''}"><nav class="u-reiter"><a href="${ziel('schueler')}" class="${reiter === 'schueler' ? 'aktiv' : ''}">Schüler*innen</a><a href="${ziel('meine')}" class="${reiter === 'meine' ? 'aktiv' : ''}">Meine Abwesenheiten</a></nav></div>` : ''}
    <div data-inhalt>${reiter === 'schueler' ? '<div class="u-kopfteil u-kopfskelett" aria-hidden="true"></div>' : ''}</div>`;
  const inhalt = seite.querySelector('[data-inhalt]');

  if (reiter === 'meine') {
    kontext.titel('Abwesenheiten', `<a class="btn btn-primary" href="#/melden/abwesenheit">${icon('plus')}Abwesenheit melden</a>`);
    const liste = (await holen('/api/app/meldungen')).filter(m => m.typ === 'absenz');
    inhalt.innerHTML = `<div class="card">${liste.length ? `<ul class="u-liste">${liste.map(m => `<li>${datumKachel(m.von)}
        <div class="u-zeilentext"><div class="titel">${e(m.grund || 'Abwesenheit')}</div><div class="unter">${e(meldungText(m))}</div>${m.text ? `<div class="small mt-1">${e(m.text)}</div>` : ''}</div>
        ${statusMarker(m.status)}</li>`).join('')}</ul>` : leer('Sie haben noch keine Abwesenheit gemeldet.', 'person-x')}</div>`;
    return;
  }

  const q = `von=${von}&bis=${bis}${klasse ? `&klasse=${encodeURIComponent(klasse)}` : ''}${status ? `&status=${status}` : ''}`;
  // Abwesenheiten als Zeiträume (von–bis, minutengenau) vom Server; die Fehlzeiten je Stunde stehen unter „Fehlzeiten“
  const [zr, gruende, fe, sl] = await Promise.all([holen(`/api/app/abwesenheiten/zeitraeume?${q}`), holen('/api/app/abwesenheitsgruende?art=abwesenheit').catch(() => []),
    // Fachlehrer-Entschuldigungen: Stand je Abwesenheit („1/3 Lehrer bearbeitet“, mehrdeutig) – nur wenn das Modul eingeschaltet ist
    holen('/api/app/entschuldigungen/einstellungen').then(x => x.aktiv ? holen(`/api/app/entschuldigungen/abwesenheiten?${q}`) : null).catch(() => null),
    // Entschuldigungsstatus mit Bedeutung ([offen], Entschuldigt, Nicht akzeptiert, Zu spät, u-1Mahn …) und Hinweis vor dem Zusammenfassen
    holen('/api/app/abwesenheiten/statusliste').catch(() => ({ status: [], zusammenfassen: '' }))]);
  const statusOptionen = aktuell => (sl.status.length ? sl.status : Object.values(STATUS_NAME).map(name => ({ name, bedeutung: '' })))
    .map(x => `<option value="${e(x.name)}" ${x.name === aktuell ? 'selected' : ''}>${e(x.name)}</option>`).join('');
  const feNach = fe ? Object.fromEntries(fe.map(x => [x.schluessel, x])) : null;
  const grundName = Object.fromEntries(gruende.map(g => [g.kurz, g.name]));
  const roh = zr.map(z => ({ ...z, datum: z.vonDatum, bis: z.bisDatum, beginn: z.vonZeit, ende: z.bisZeit, bemerkung: z.text, stundenSumme: z.fehlstunden }));
  const liste = roh.filter(z => (!grund || z.grund === grund) && (!art || z.art === art));
  // Sortierung wie im Original („Klasse, Schüler*in, Datum“ …), je Sitzung gemerkt
  const SORTIERUNGEN = { ksd: 'Klasse, Schüler*in, Datum', dks: 'Datum, Klasse, Schüler*in', sd: 'Schüler*in, Datum' };
  let sortierung = 'ksd';
  try { sortierung = SORTIERUNGEN[sessionStorage.getItem('untplan:abw-sortierung')] ? sessionStorage.getItem('untplan:abw-sortierung') : 'ksd'; } catch { /* privat */ }
  const vergleich = { ksd: (x, y) => x.klasse.localeCompare(y.klasse, 'de') || x.schueler.localeCompare(y.schueler, 'de') || `${x.datum}${x.beginn}`.localeCompare(`${y.datum}${y.beginn}`),
    dks: (x, y) => `${x.datum}${x.beginn}`.localeCompare(`${y.datum}${y.beginn}`) || x.klasse.localeCompare(y.klasse, 'de') || x.schueler.localeCompare(y.schueler, 'de'),
    sd: (x, y) => x.schueler.localeCompare(y.schueler, 'de') || `${x.datum}${x.beginn}`.localeCompare(`${y.datum}${y.beginn}`) };
  if (schuelerWahl) liste.splice(0, liste.length, ...liste.filter(z => z.schuelerId === schuelerWahl));
  liste.sort(vergleich[sortierung]);
  const kinder = klasse ? await holen(`/api/app/klassenbuch/schueler?klasse=${encodeURIComponent(klasse)}`).catch(() => []) : [];
  const leitung = k => (el.klassen.find(x => x.kurz === k)?.leitung ?? []).join(', ');
  const verwalten = roh.some(z => z.verwalten) || ich.admin;
  const sj = kontext.sitzung?.schuljahr ?? kontext.schuljahr ?? {};
  kontext.titel('Abwesenheiten', '', `${liste.length}`);
  const artSymbol = z => z.art === 'verspaetet' ? `<i class="bi bi-clock abw-art gelb" title="verspätet ${z.minuten} min" aria-label="verspätet ${z.minuten} min"></i>`
    : z.art === 'vorzeitig' ? `<i class="bi bi-box-arrow-right abw-art lila" title="vorzeitig gegangen ${z.minuten} min" aria-label="vorzeitig gegangen ${z.minuten} min"></i>`
    : `<span class="abw-art" title="Abwesenheit">${icon('abwesenheiten')}</span>`;
  const nummer = z => z.status !== 'offen' || z.statusName ? `<span class="kl-nr"> / ${e(String(z.ids?.[0] ?? ''))}</span>` : '';
  // Kopf wie im Original: Klasse | Schüler*innen | Suche | Zeitraum, darunter Abwesenheitsgrund | Status | Sortierung
  inhalt.innerHTML = `
    <div class="nl-leiste sg-leiste u-kopfteil abw-leiste">
      <label class="sg-feld"><span>Klasse</span><select class="form-select" data-klasse><option value="">- Alle -</option>${el.klassen.map(k => `<option ${k.kurz === klasse ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select></label>
      <label class="sg-feld"><span>Schüler*innen</span><select class="form-select" data-schueler ${klasse ? '' : 'disabled'}><option value="">- Alle -</option>${kinder.map(k => `<option value="${k.id}" ${k.id === schuelerWahl ? 'selected' : ''}>${e(k.name)} ${e(k.vorname)}</option>`).join('')}</select></label>
      <span class="sg-feld"><span>Suche</span>${suchfeldMitFilter({ platzhalter: 'Suchen', label: 'Schüler*in suchen', klasse: 'abw-suche' })}</span>
      ${zeitbereichFeld(von, bis, sj)}
      <label class="sg-feld"><span>Abwesenheitsgrund</span><select class="form-select" data-grund><option value="">- Alle -</option>${gruende.map(g => `<option value="${e(g.kurz)}" ${g.kurz === grund ? 'selected' : ''}>${e(g.name)}</option>`).join('')}</select></label>
      <label class="sg-feld"><span>Status</span><select class="form-select" data-status><option value="">- Alle -</option>${[['offen', '[offen]'], ['entschuldigt', 'entschuldigt'], ['unentschuldigt', 'nicht entschuldigt']].map(([k, t]) => `<option value="${k}" ${k === status ? 'selected' : ''}>${t}</option>`).join('')}</select></label>
      <label class="sg-feld"><span>Sortierung</span><select class="form-select" data-sortierung>${Object.entries(SORTIERUNGEN).map(([k, t]) => `<option value="${k}" ${k === sortierung ? 'selected' : ''}>${t}</option>`).join('')}</select></label>
      <div class="u-fl-aktionen kl-knoepfe">
        ${verwalten ? `<button type="button" class="nl-knopf" data-neu>Neu</button><button type="button" class="nl-knopf" data-sammel-loeschen>Löschen</button>
          <button type="button" class="nl-knopf" data-zusammen>Zusammenfassen</button><button type="button" class="nl-knopf" data-setzen="entschuldigt">Entschuldigen</button>` : ''}
        <details class="kl-menue"><summary class="nl-knopf">Berichte <i class="bi bi-caret-down-fill"></i></summary>
          <div class="kl-menue-liste"><a href="#/berichte/abwesenheiten/${encodeURIComponent(klasse || '-')}/${von}/${bis}">Abwesenheiten</a>
            <a href="#/berichte/fehlzeiten_schueler/${encodeURIComponent(klasse || '-')}/${von}/${bis}">Fehlzeiten pro Schüler*in</a>
            <button type="button" data-csv>CSV-Export</button></div></details>
      </div>
    </div>
    <div class="nl-tabelle abw-tabelle">${liste.length ? `<table><thead><tr>${verwalten ? '<th class="nl-wahl"><span class="sg-kopfwahl">Auswahl<input type="checkbox" class="nl-rund" data-alle aria-label="Alle auswählen"></span></th><th></th>' : ''}
        <th>Art</th><th>Benutzer</th><th>Schüler*innen</th><th>Klasse</th><th>Kl-Lehrkräfte</th><th>Von</th><th>Beginn</th><th>Bis</th><th>Ende</th><th>Grund</th><th>Status</th><th>Text</th></tr></thead><tbody>
      ${liste.map((z, i) => `<tr data-name="${e((z.schueler + ' ' + z.klasse + ' ' + z.bemerkung).toLowerCase())}">${verwalten ? `<td class="nl-wahl">${z.verwalten || ich.admin ? `<input type="checkbox" class="nl-rund" data-i="${i}" aria-label="auswählen">` : ''}</td>
        <td class="kl-zelle-icons">${z.verwalten || ich.admin ? `<button class="nl-icon sichtbar" data-bearbeiten="${i}" title="Bearbeiten" aria-label="Bearbeiten">${icon('stift')}</button>${z.loeschbar !== false ? `<button class="nl-icon sichtbar rot" data-loeschen="${i}" title="Löschen" aria-label="Löschen"><i class="bi bi-x-lg"></i></button>` : ''}<button type="button" class="nl-icon sichtbar" data-entsch="${i}" title="Entschuldigung" aria-label="Entschuldigung"><i class="bi bi-envelope-paper"></i></button>` : ''}</td>` : ''}
        <td>${artSymbol(z)}</td><td>${e(z.lehrer.join(', '))}</td><td class="text-nowrap">${e(z.schueler)}</td><td>${e(z.klasse)}</td><td>${e(leitung(z.klasse))}</td>
        <td class="text-nowrap">${datumDe(z.datum)}</td><td>${e(z.beginn)}</td><td class="text-nowrap">${datumDe(z.bis)}</td><td>${e(z.ende)}</td>
        <td title="${e(grundName[z.grund] ?? '')}">${e(z.grund || '')}</td>
        <td class="text-nowrap">${z.statusGemischt ? `<span class="u-chip gelb" title="${z.entschuldigt} entschuldigt, ${z.unentschuldigt} nicht entschuldigt, ${z.fehlstunden - z.entschuldigt - z.unentschuldigt} offen (wie in den Fehlzeiten)">teilweise</span>` : (z.verwalten || ich.admin ? statusKnopf(z, i) : STATUS[z.status] ?? e(z.status))}${nummer(z)}${feNach?.[z.schluessel]?.mehrdeutig ? '<div class="fe-mehrdeutig">(mehrdeutig)</div>' : ''}
          ${feNach?.[z.schluessel]?.lehrerGesamt ? `<div><button type="button" class="fe-link" data-fe="${e(z.schluessel)}" title="Fehlzeiten der Abwesenheit anzeigen">${feNach[z.schluessel].lehrerBearbeitet}/${feNach[z.schluessel].lehrerGesamt} Lehrer bearbeitet</button></div>` : ''}</td>
        <td class="nl-text">${e(z.bemerkung)}${detailSymbole(z) ? `<div class="nl-chips mt-1">${detailSymbole(z)}</div>` : ''}</td></tr>`).join('')}
      </tbody></table>
      <div class="sg-fuss"><span>${liste.length === 1 ? 'Ein Element gefunden.' : `${liste.length} Elemente gefunden, Anzeige aller Elemente.`}</span></div>`
      : leer('Nichts anzuzeigen')}</div>`;
  const f = n => inhalt.querySelector(`[data-${n}]`)?.value ?? '';
  suchfeldMitFilterBinden(inhalt, {
    beiSuche: t => { t = t.toLowerCase(); inhalt.querySelectorAll('.nl-tabelle tbody tr').forEach(tr => { tr.hidden = t.length > 0 && !tr.dataset.name.includes(t); }); },
  });
  const filterNeu = (v = f('von') || von, b = f('bis') || bis) => kontext.navigieren(ziel('schueler', v, b, f('klasse'), f('status'), f('grund'), art, f('klasse') === klasse && f('schueler') ? Number(f('schueler')) : null));
  ['klasse', 'schueler', 'grund', 'status'].forEach(n => { inhalt.querySelector(`[data-${n}]`).onchange = () => filterNeu(); });
  zeitbereichBinden(inhalt, sj, filterNeu);
  inhalt.querySelector('[data-sortierung]').onchange = ev => { try { sessionStorage.setItem('untplan:abw-sortierung', ev.target.value); } catch { /* privat */ } kontext.navigieren(location.hash); };
  inhalt.querySelector('[data-alle]')?.addEventListener('change', ev => inhalt.querySelectorAll('[data-i]').forEach(c => { c.checked = ev.target.checked; }));
  const gewaehlt = () => [...inhalt.querySelectorAll('[data-i]:checked')].map(c => liste[Number(c.dataset.i)]);
  const neu = () => kontext.navigieren(location.hash);
  const texte = await holen('/api/app/abwesenheiten/texte').catch(() => []);
  const textListe = `<datalist id="abw-texte">${texte.map(t => `<option value="${e(t)}">`).join('')}</datalist>`;

  inhalt.querySelectorAll('[data-setzen]').forEach(b => b.onclick = async () => {
    const auswahl = gewaehlt();
    if (!auswahl.length) return toast('Bitte zuerst Abwesenheiten auswählen.', true);
    const ids = auswahl.flatMap(z => z.ids);
    // Fachlehrer-Entschuldigungen eingeschaltet: Entschuldigung der ganzen Abwesenheit mit Text und Warnung beim Überschreiben
    if (feNach && auswahl.every(z => feNach[z.schluessel])) {
      const { abwesenheitDialog } = await import('./entschuldigungen.js');
      abwesenheitDialog(auswahl.map(z => feNach[z.schluessel]), { status: b.dataset.setzen, nachweise: NACHWEISE, fertig: neu });
      return;
    }
    if (b.dataset.setzen !== 'entschuldigt') {
      try { await senden('/api/app/klassenbuch/fehlzeiten/status', { ids, status: b.dataset.setzen }); toast('Status geändert.'); neu(); } catch (err) { toast(err.message, true); }
      return;
    }
    dialog({ titel: 'Entschuldigen', inhalt: `<p class="small">${auswahl.length} Abwesenheit(en) entschuldigen.</p><label class="form-label">Text</label><input class="form-control" name="text" list="abw-texte" placeholder="z. B. Attest liegt vor">${textListe}
        <div class="row g-3 mt-0"><div class="col-sm-6"><label class="form-label">Nachweis</label><select class="form-select" name="nachweis"><option value="">kein Nachweis</option>${Object.entries(NACHWEISE).map(([k, t]) => `<option value="${k}">${t}</option>`).join('')}</select></div>
        <div class="col-sm-6"><label class="form-label">Eingegangen am</label><input type="date" class="form-control" name="entschuldigtAm" value="${heuteIso()}"></div></div>`,
      knoepfe: [{ text: 'Abbrechen' }, { text: 'Entschuldigen', art: 'haupt', aktion: async box => {
        const t = box.querySelector('[name=text]').value.trim();
        const nw = box.querySelector('[name=nachweis]').value, am = box.querySelector('[name=entschuldigtAm]').value;
        await senden('/api/app/abwesenheiten', { ids, status: 'entschuldigt', bemerkung: t || null, nachweis: nw || null, entschuldigtAm: am || null }, 'PUT');
        toast('Entschuldigt.'); neu();
      } }] });
  });
  inhalt.querySelectorAll('[data-fe]').forEach(b => b.onclick = async () => {
    const { seitenleiste } = await import('./entschuldigungen.js');
    seitenleiste(feNach[b.dataset.fe], g => grundName[g] ?? g);
  });
  // Zusammenfassen wie im Original: unabhängig von gesetzten Häkchen werden alle dargestellten Abwesenheiten im eingestellten Zeitbereich
  // möglichst zusammengefasst (gleicher Abwesenheitsgrund, passender Entschuldigungsstatus, keine Anwesenheit dazwischen)
  inhalt.querySelector('[data-zusammen]')?.addEventListener('click', async () => {
    if (!await bestaetigen('Zusammenfassen', sl.zusammenfassen || 'Abwesenheiten im gewählten Zeitbereich zusammenfassen?', 'Zusammenfassen')) return;
    try {
      const r = await senden('/api/app/abwesenheiten/zusammenfassen-bereich', { von, bis, klasse: klasse || null });
      toast(r.abwesenheiten ? `${r.abwesenheiten} ${r.abwesenheiten === 1 ? 'Abwesenheit' : 'Abwesenheiten'} zusammengefasst.` : 'Keine Abwesenheiten zum Zusammenfassen.'); neu();
    } catch (err) { toast(err.message, true); }
  });
  // Klick auf den Status (blaues Fragezeichen, „entsch.“, „nicht entsch.“): Entschuldigung mit Status, Datum und Text, darunter die Abwesenheit mit Grund
  inhalt.querySelectorAll('[data-entsch]').forEach(b => b.onclick = () => {
    const z = liste[Number(b.dataset.entsch)];
    const aktuell = z.statusName || STATUS_NAME[z.status] || '[offen]';
    dialog({ titel: `Entschuldigung für ${z.schueler}`, inhalt: `<div class="row g-3">
        <div class="col-sm-7"><label class="form-label">Entschuldigungsnummer</label><input class="form-control" value="${e(z.status !== 'offen' || z.statusName ? String(z.ids?.[0] ?? '') : '0')}" readonly></div>
        <div class="col-sm-5"><label class="form-label">Datum</label><input type="date" class="form-control" name="entschuldigtAm" value="${e(z.entschuldigtAm || heuteIso())}"></div>
        <div class="col-12"><label class="form-label">Status</label><select class="form-select" name="status">${statusOptionen(aktuell)}</select></div>
        <div class="col-12"><div class="abw-details-titel">Abwesenheit</div><div class="small">${e(z.schueler)}, ${datumDe(z.datum)}${z.bis !== z.datum ? ` – ${datumDe(z.bis)}` : ''}, ${e(z.beginn)} – ${e(z.ende)}</div></div>
        <div class="col-12"><label class="form-label">Abwesenheitsgrund</label><select class="form-select" name="grund"><option value="">&lt;Auswahl&gt;</option>${gruende.map(g => `<option value="${e(g.kurz)}" ${g.kurz === z.grund ? 'selected' : ''}>${e(g.name)}</option>`).join('')}</select></div>
        <div class="col-12"><label class="form-label">Text</label><input class="form-control" name="bemerkung" value="${e(z.bemerkung)}" list="abw-texte" maxlength="500">${textListe}</div></div>`,
      knoepfe: [{ text: 'Speichern', art: 'haupt', aktion: async box => {
        const v = n => box.querySelector(`[name=${n}]`).value;
        await senden('/api/app/abwesenheiten', { ids: z.ids, status: v('status'), grund: v('grund'), bemerkung: v('bemerkung'), entschuldigtAm: v('entschuldigtAm') || null }, 'PUT');
        toast('Gespeichert.'); neu();
      } }, { text: 'Schließen' }] });
  });
  inhalt.querySelector('[data-sammel-loeschen]')?.addEventListener('click', async () => {
    const auswahl = gewaehlt().filter(z => z.loeschbar !== false);
    if (!auswahl.length) return toast('Bitte zuerst Abwesenheiten auswählen.', true);
    if (!await bestaetigen('Abwesenheiten löschen', `${auswahl.length === 1 ? 'Die ausgewählte Abwesenheit' : `${auswahl.length} Abwesenheiten`} löschen?`)) return;
    await senden('/api/app/abwesenheiten/loeschen', { ids: auswahl.flatMap(z => z.ids) });
    toast('Gelöscht.'); neu();
  });
  inhalt.querySelectorAll('[data-loeschen]').forEach(b => b.onclick = async () => {
    const z = liste[Number(b.dataset.loeschen)];
    if (!await bestaetigen('Abwesenheit löschen', `Abwesenheit von ${z.schueler} ${z.bis !== z.datum ? `vom ${datumDe(z.datum)} bis ${datumDe(z.bis)}` : `am ${datumDe(z.datum)}`} löschen?`)) return;
    await senden('/api/app/abwesenheiten/loeschen', { ids: z.ids });
    toast('Gelöscht.'); neu();
  });
  // wie im Original: Von | Bis (Datum), darunter Beginnzeit | Endzeit
  const zeitfelder = (z = {}) => `
        <div class="col-6"><label class="form-label">Von</label><input type="date" class="form-control" name="vonDatum" value="${e(z.datum || heuteIso())}"></div>
        <div class="col-6"><label class="form-label">Bis</label><input type="date" class="form-control" name="bisDatum" value="${e(z.bis || heuteIso())}"></div>
        <div class="col-6"><label class="form-label">Beginnzeit</label><input type="time" step="60" class="form-control" name="vonZeit" value="${e(z.beginn || (el.stunden ?? [])[0]?.beginn || '07:45')}"></div>
        <div class="col-6"><label class="form-label">Endzeit</label><input type="time" step="60" class="form-control" name="bisZeit" value="${e(z.ende || (el.stunden ?? []).at(-1)?.ende || '16:00')}"></div>
        <div class="col-12"><div class="form-check form-switch"><input class="form-check-input" type="checkbox" role="switch" id="abw-ganz" name="ganztaegig" ${z.ganztaegig ? 'checked' : ''}>
          <label class="form-check-label" for="abw-ganz">Ganztägig</label></div></div>`;
  // Ganztägig: Uhrzeiten auf den ganzen Schultag setzen und sperren; der Server trägt alle Stunden der Tage ein
  const tagesbeginn = (el.stunden ?? [])[0]?.beginn || '07:45', tagesende = (el.stunden ?? []).at(-1)?.ende || '16:00';
  const istGanztaegig = z => !!z.beginn && !!z.ende && z.beginn <= tagesbeginn && z.ende >= tagesende;
  const ganzBinden = box => {
    const sw = box.querySelector('[name=ganztaegig]');
    const setzen = () => {
      ['vonZeit', 'bisZeit'].forEach(n => { box.querySelector(`[name=${n}]`).disabled = sw.checked; });
      if (sw.checked) { box.querySelector('[name=vonZeit]').value = tagesbeginn; box.querySelector('[name=bisZeit]').value = tagesende; }
    };
    sw.onchange = setzen; setzen();
  };
  const zeitWerte = box => { const v = n => box.querySelector(`[name=${n}]`).value; const ganz = box.querySelector('[name=ganztaegig]')?.checked;
    return { von: `${v('vonDatum')}T${v('vonZeit')}`, bis: `${v('bisDatum')}T${v('bisZeit')}`, ganztaegig: ganz || null }; };
  const abwSpeichern = (box, z) => {
    const v = n => box.querySelector(`[name=${n}]`).value;
    return senden('/api/app/klassenbuch/abwesenheit', { schuelerIds: [z.schuelerId], ...zeitWerte(box), ersetzen: z.ids, grund: v('grund'), status: v('status'), text: v('bemerkung'), ...detailWerte(box) });
  };
  inhalt.querySelectorAll('[data-bearbeiten]').forEach(b => b.onclick = () => {
    const z = liste[Number(b.dataset.bearbeiten)];
    // Stift: Abwesenheit bearbeiten (Von/Bis, Grund, Text) und direkt im selben Dialog entschuldigen (Entschuldigungsstatus, Nachweis, Eingang)
    // Stift wie im Original „Abwesenheit“: Schüler*innen, Von | Bis, Beginnzeit | Endzeit, Abwesenheitsgrund, Text, Gemeldet an Erziehungsberechtigter,
    // Aufgegeben/Geändert am … von …; Speichern | Löschen | Schließen (dazu die Entschuldigung der Abwesenheit)
    const zeit = s => s ? `${s.slice(8, 10)}.${s.slice(5, 7)}.${s.slice(0, 4)}${s.slice(10, 19).replace('T', ' ')}` : '';
    dialog({ titel: 'Abwesenheit', breit: true, inhalt: `<div class="row g-3" data-abw-bearbeiten>
        <div class="col-12"><label class="form-label">Schüler*innen</label><input class="form-control" value="${e(z.schueler)}" readonly></div>
        ${zeitfelder({ ...z, ganztaegig: istGanztaegig(z) })}
        <div class="col-12"><label class="form-label">Abwesenheitsgrund</label><select class="form-select" name="grund"><option value="">&lt;Auswahl&gt;</option>${gruende.map(g => `<option value="${e(g.kurz)}" ${g.kurz === z.grund ? 'selected' : ''}>${e(g.name)}</option>`).join('')}</select></div>
        <div class="col-12"><label class="form-label">Text</label><input class="form-control" name="bemerkung" value="${e(z.bemerkung)}" list="abw-texte" maxlength="500">${textListe}</div>
        <div class="col-12 abw-gemeldet"><span>Gemeldet an</span><label class="form-check m-0"><input class="form-check-input" type="checkbox" name="elternInformiert" ${z.elternInformiert ? 'checked' : ''}><span class="form-check-label">Erziehungsberechtigter</span></label></div>
        ${z.erstellt ? `<div class="col-12 small text-body-secondary abw-protokoll"><span>Aufgegeben am</span><span>${e(zeit(z.erstellt))}</span><span>${z.lehrer.length ? `von ${e(z.lehrer.join(', '))}` : ''}</span></div>` : ''}
        <div class="col-12"><div class="abw-details-titel">Entschuldigung</div></div>
        <div class="col-sm-6"><label class="form-label">Entschuldigungsstatus</label><select class="form-select" name="status" data-entsch-status>${statusOptionen(z.statusName || STATUS_NAME[z.status])}</select></div>
        ${detailFelder(z).replace('<div class="col-12"><div class="abw-details-titel">Weitere Angaben</div></div>', '').replace(/<div class="col-12"><div class="form-check form-switch"><input class="form-check-input" type="checkbox" role="switch" id="abw-eltern"[\s\S]*?<\/label><\/div><\/div>/, '')}</div>`,
      beimOeffnen: ganzBinden,
      knoepfe: [
        { text: 'Speichern', art: 'haupt', aktion: async box => { await abwSpeichern(box, z); toast('Gespeichert.'); neu(); } },
        // „Entschuldigen“: Status „Entschuldigt“, Eingang heute (falls leer), dann speichern
        { text: 'Entschuldigen', aktion: async box => {
          box.querySelector('[data-entsch-status]').value = 'Entschuldigt';
          const am = box.querySelector('[name=entschuldigtAm]');
          if (am && !am.value) am.value = heuteIso();
          await abwSpeichern(box, z); toast('Entschuldigt.'); neu();
        } },
        ...(z.loeschbar !== false ? [{ text: 'Löschen', art: 'gefahr', aktion: async () => {
          if (!await bestaetigen('Abwesenheit löschen', `Abwesenheit von ${z.schueler} löschen?`)) return false;
          await senden('/api/app/abwesenheiten/loeschen', { ids: z.ids }); toast('Gelöscht.'); neu();
        } }] : []),
        { text: 'Schließen' }] });
  });
  document.querySelector('[data-neu]')?.addEventListener('click', async () => {
    const kl = klasse || el.klassen.find(k => ich.element && (k.leitung ?? []).includes(ich.element))?.kurz || el.klassen[0]?.kurz;
    const schueler = kl ? await holen(`/api/app/klassenbuch/schueler?klasse=${encodeURIComponent(kl)}`) : [];
    dialog({ titel: 'Neue Abwesenheit', breit: true,
      inhalt: `<div class="row g-3">
        <div class="col-sm-4"><label class="form-label">Klasse</label><select class="form-select" name="klasse">${el.klassen.map(k => `<option ${k.kurz === kl ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select></div>
        <div class="col-sm-8"><label class="form-label">Schüler*innen</label><select class="form-select" name="schuelerId">${schueler.map(s => `<option value="${s.id}">${e(s.name)} ${e(s.vorname)}</option>`).join('')}</select></div>
        ${zeitfelder()}
        <div class="col-sm-6"><label class="form-label">Abwesenheitsgrund</label><select class="form-select" name="grund"><option value="">&lt;Auswahl&gt;</option>${gruende.map(g => `<option value="${e(g.kurz)}" ${g.kurz === 'u' ? 'selected' : ''}>${e(g.name)}</option>`).join('')}</select></div>
        <div class="col-sm-6"><label class="form-label">Status</label><select class="form-select" name="status"><option value="offen">offen</option><option value="entschuldigt">entschuldigt</option><option value="unentschuldigt">nicht entschuldigt</option></select></div>
        <div class="col-12"><label class="form-label">Text</label><input class="form-control" name="bemerkung" list="abw-texte">${textListe}</div>
        ${detailFelder()}</div>`,
      beimOeffnen: box => {
        ganzBinden(box);
        box.querySelector('[name=klasse]').onchange = async ev => {
          const s = await holen(`/api/app/klassenbuch/schueler?klasse=${encodeURIComponent(ev.target.value)}`);
          box.querySelector('[name=schuelerId]').innerHTML = s.map(x => `<option value="${x.id}">${e(x.name)} ${e(x.vorname)}</option>`).join('');
        };
      },
      knoepfe: [{ text: 'Speichern', art: 'haupt', aktion: async box => {
        const v = n => box.querySelector(`[name=${n}]`).value;
        const r = await senden('/api/app/klassenbuch/abwesenheit', { schuelerIds: [Number(v('schuelerId'))], ...zeitWerte(box), grund: v('grund'), status: v('status'), text: v('bemerkung'), ...detailWerte(box) });
        toast(`${r.stunden} ${r.stunden === 1 ? 'Fehlstunde' : 'Fehlstunden'} eingetragen.`); neu();
      } }, { text: 'Abbrechen' }] });
  });
  document.querySelector('[data-csv]')?.addEventListener('click', () => herunterladen(`abwesenheiten-${von}-${bis}.csv`,
    ['Schüler*in;Klasse;Von;Bis;Fehlstunden;Art;Minuten;Grund;Fach;Status;Text;Nachweis;Entschuldigung am;Eltern informiert;erfasst von', ...liste.map(z => [z.schueler, z.klasse, `${z.datum} ${z.beginn}`, `${z.bis} ${z.ende}`, z.fehlstunden, z.art, z.art === 'fehlt' ? z.fehlminuten : z.minuten, grundName[z.grund] ?? z.grund, z.faecher.join(', '), z.status, z.bemerkung, NACHWEISE[z.nachweis] ?? '', z.entschuldigtAm, z.elternInformiert ? 'ja' : '', z.lehrer.join(', ')]
      .map(x => `"${String(x ?? '').replace(/"/g, '""')}"`).join(';'))].join('\r\n')));
}
