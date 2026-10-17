// Klassenbuch → Abwesenheiten wie im Original: Liste der Schülerabwesenheiten mit Filter (Klasse, Schüler*in, Zeitraum, Grund, Status),
// Neu/Bearbeiten/Löschen, Zusammenfassen und Entschuldigen durch die Klassenleitung, CSV; Reiter „Meine Abwesenheiten“ (eigene Meldungen).
import { e, icon, holen, senden, api, toast, leer, filterFeld, datumKachel, stundenText, heuteIso, plusTage, meldungText, statusMarker, herunterladen, infoHinweis, suchfeldMitFilter, suchfeldMitFilterBinden } from '../ui.js';
import { kbDialog as dialog, kbBestaetigen as bestaetigen } from '../kbdialog.js';

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
  z.nachweis ? `<span class="nl-tag" title="Nachweis: ${e(NACHWEISE[z.nachweis] ?? z.nachweis)}${z.entschuldigtAm ? ` · eingegangen ${datumDe(z.entschuldigtAm)}` : ''}"><i class="bi bi-file-earmark-check me-1"></i>${e((NACHWEISE[z.nachweis] ?? z.nachweis).replace('ärztliches ', '').replace(' Entschuldigung', ''))}</span>` : '',
  z.elternInformiert ? '<span class="nl-tag" title="Eltern informiert"><i class="bi bi-telephone me-1"></i>Eltern</span>' : '',
  z.notizIntern ? `<span class="nl-tag" title="${e(z.notizIntern)}"><i class="bi bi-lock me-1"></i>Notiz</span>` : '',
].filter(Boolean).join(' ');

const ARTEN = { fehlt: 'abwesend', verspaetet: 'verspätet', vorzeitig: 'vorzeitig gegangen' };
const artChip = z => z.art === 'verspaetet' ? `<span class="u-chip gelb"><i class="bi bi-clock me-1"></i>verspätet ${z.minuten} min</span>`
  : z.art === 'vorzeitig' ? `<span class="u-chip lila"><i class="bi bi-box-arrow-right me-1"></i>vorzeitig ${z.minuten} min</span>`
  : /^(verspätet|vorzeitig gegangen)/.test(z.bemerkung || '') ? `abwesend <span class="small text-body-secondary">(${e(z.bemerkung.match(/^(verspätet|vorzeitig gegangen)/)[1])})</span>` : 'abwesend';

export async function zeigen(seite, kontext, [reiterParam, vonParam, bisParam, klasseParam, statusParam, grundParam, artParam]) {
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
  const ziel = (r, v = von, b = bis, k = klasse, s = status, g = grund, a = art) => `#/abwesenheiten/${r}/${v}/${b}/${encodeURIComponent(k || '-')}/${s || '-'}/${encodeURIComponent(g || '-')}${a ? `/${a}` : ''}`;
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
  const [zr, gruende, fe] = await Promise.all([holen(`/api/app/abwesenheiten/zeitraeume?${q}`), holen('/api/app/abwesenheitsgruende?art=abwesenheit').catch(() => []),
    // Fachlehrer-Entschuldigungen: Stand je Abwesenheit („1/3 Lehrer bearbeitet“, mehrdeutig) – nur wenn das Modul eingeschaltet ist
    holen('/api/app/entschuldigungen/einstellungen').then(x => x.aktiv ? holen(`/api/app/entschuldigungen/abwesenheiten?${q}`) : null).catch(() => null)]);
  const feNach = fe ? Object.fromEntries(fe.map(x => [x.schluessel, x])) : null;
  const grundName = Object.fromEntries(gruende.map(g => [g.kurz, g.name]));
  const roh = zr.map(z => ({ ...z, datum: z.vonDatum, bis: z.bisDatum, beginn: z.vonZeit, ende: z.bisZeit, bemerkung: z.text, stundenSumme: z.fehlstunden }));
  const liste = roh.filter(z => (!grund || z.grund === grund) && (!art || z.art === art));
  const anzahl = { offen: roh.filter(z => z.status === 'offen').length, unent: roh.filter(z => z.status === 'unentschuldigt').length,
    spaet: roh.filter(z => z.art === 'verspaetet').length, frueh: roh.filter(z => z.art === 'vorzeitig').length,
    minuten: roh.filter(z => z.art !== 'fehlt').reduce((n, z) => n + z.minuten, 0) };
  const verwalten = roh.some(z => z.verwalten) || ich.admin;
  // Bericht und Hinzufügen oben rechts im Kopf (die Suchleiste bleibt einzeilig)
  kontext.titel('Abwesenheiten', `<button type="button" class="btn btn-outline-secondary" data-csv title="Als CSV herunterladen">${icon('csv')}Bericht</button>
    ${verwalten ? `<button type="button" class="btn btn-primary" data-neu>${icon('plus')}Hinzufügen</button>` : ''}`, `${liste.length}`);
  const pille = (name, inhaltHtml, aktiv) => `<label class="nl-pille ${aktiv ? 'aktiv' : ''}"><span class="visually-hidden">${name}</span>${inhaltHtml}</label>`;
  inhalt.innerHTML = `
    <div class="nl-leiste u-kopfteil abw-leiste">
      ${suchfeldMitFilter({ platzhalter: 'Suchen', label: 'Schüler*in suchen', filter: [
        { name: 'klasse', label: 'Klasse', alle: 'Alle Klassen', wert: klasse, optionen: el.klassen.map(k => [k.kurz, k.kurz]) },
        { name: 'status', label: 'Status', alle: 'Jeder Status', wert: status, optionen: [['offen', 'offen'], ['entschuldigt', 'entschuldigt'], ['unentschuldigt', 'nicht entschuldigt']] },
        { name: 'art', label: 'Art', alle: 'Alle Arten', wert: art, optionen: Object.entries(ARTEN) },
        { name: 'grund', label: 'Abwesenheitsgrund', alle: 'Alle Gründe', wert: grund, optionen: gruende.map(g => [g.kurz, g.name]) },
      ] })}
      ${pille('Von', `<input type="date" data-von value="${von}">`, false)}
      ${pille('Bis', `<input type="date" data-bis value="${bis}">`, false)}
      ${infoHinweis(`Eine <b>Abwesenheit</b> ist der Zeitraum, in dem eine Schülerin bzw. ein Schüler fehlt (z. B. krank von Mittwoch bis Freitag). Wie viele Unterrichtsstunden daraus laut Stundenplan entstehen, zeigen die <a href="#/klassenbuch/${encodeURIComponent(klasse || gemeinsam?.klasse || el.klassen[0]?.kurz || '')}/fehlzeiten/${bis}/${von}">Fehlzeiten</a> – mit denselben Daten.`)}
    </div>
    <div class="nl-zahlen kompakt">
      <button type="button" class="nl-zahl ${status === 'offen' ? 'aktiv' : ''}" data-kz="offen"><b>${anzahl.offen}</b> offen</button>
      <button type="button" class="nl-zahl rot ${status === 'unentschuldigt' ? 'aktiv' : ''}" data-kz="unentschuldigt"><b>${anzahl.unent}</b> nicht entschuldigt</button>
      <button type="button" class="nl-zahl gelb ${art === 'verspaetet' ? 'aktiv' : ''}" data-kz="verspaetet"><b>${anzahl.spaet}</b> Verspätungen</button>
      <button type="button" class="nl-zahl lila ${art === 'vorzeitig' ? 'aktiv' : ''}" data-kz="vorzeitig"><b>${anzahl.frueh}</b> vorzeitig gegangen</button>
      <span class="nl-zahl still"><b>${anzahl.minuten}</b> Minuten</span>
    </div>
    <div class="nl-tabelle">${liste.length ? `<table><thead><tr>${verwalten ? '<th class="nl-wahl"><input type="checkbox" class="nl-rund" data-alle aria-label="Alle auswählen"></th>' : ''}
        <th colspan="2">Name</th><th>Zeitraum</th><th>Art</th><th>Grund</th><th>Status</th>${feNach ? '<th>Fehlzeiten</th>' : ''}<th>Text</th>${verwalten ? '<th></th>' : ''}</tr></thead><tbody>
      ${liste.map((z, i) => `<tr data-name="${e((z.schueler + ' ' + z.klasse).toLowerCase())}">${verwalten ? `<td class="nl-wahl">${z.verwalten || ich.admin ? `<input type="checkbox" class="nl-rund" data-i="${i}" aria-label="auswählen">` : ''}</td>` : ''}
        <td class="nl-avatar-zelle">${avatar(z.schueler, z.status === 'offen')}</td>
        <td><div class="nl-name">${e(z.schueler)}</div><div class="nl-unter">${e(z.klasse)}${z.lehrer.length ? ` · erfasst von ${e(z.lehrer.join(', '))}` : ''}</div></td>
        <td class="text-nowrap"><div>${tagKurz(z.datum)} <span class="nl-unter">${e(z.beginn)}</span> – ${z.bis !== z.datum ? `${tagKurz(z.bis)} ` : ''}<span class="nl-unter">${e(z.ende)}</span></div>
          <div class="nl-unter">${z.fehlstunden} ${z.fehlstunden === 1 ? 'Fehlstunde' : 'Fehlstunden'} · ${z.fehlminuten} min</div></td>
        <td>${artChip(z)}</td>
        <td>${z.grund ? `<span class="nl-tag">${e(grundName[z.grund] ?? z.grund)}</span>` : ''}</td><td>${z.statusGemischt ? `<span class="u-chip gelb" title="${z.entschuldigt} entschuldigt, ${z.unentschuldigt} nicht entschuldigt, ${z.fehlstunden - z.entschuldigt - z.unentschuldigt} offen (wie in den Fehlzeiten)">teilweise</span><div class="nl-unter">${z.entschuldigt}/${z.fehlstunden} entschuldigt</div>` : STATUS[z.status] ?? e(z.status)}${feNach?.[z.schluessel]?.mehrdeutig ? '<div class="fe-mehrdeutig">(mehrdeutig)</div>' : ''}</td>
        ${feNach ? `<td>${feNach[z.schluessel]?.lehrerGesamt ? `<button type="button" class="fe-link" data-fe="${e(z.schluessel)}" title="Fehlzeiten der Abwesenheit anzeigen">${feNach[z.schluessel].lehrerBearbeitet}/${feNach[z.schluessel].lehrerGesamt} Lehrer bearbeitet</button>` : ''}</td>` : ''}<td class="nl-text">${e(z.bemerkung)}${detailSymbole(z) ? `<div class="nl-chips mt-1">${detailSymbole(z)}</div>` : ''}</td>
        ${verwalten ? `<td class="text-end text-nowrap nl-aktionen">${z.verwalten || ich.admin ? `<button class="nl-icon" data-bearbeiten="${i}" title="Bearbeiten" aria-label="Bearbeiten">${icon('stift')}</button>
          <button class="nl-icon rot" data-loeschen="${i}" title="Löschen" aria-label="Löschen">${icon('loeschen')}</button>` : ''}</td>` : ''}</tr>`).join('')}
      </tbody></table>
      ${verwalten ? `<div class="nl-fuss"><span class="nl-unter me-2">${liste.length} Elemente</span>
        <button class="nl-knopf gruen" data-setzen="entschuldigt">Entschuldigen</button><button class="nl-knopf rot" data-setzen="unentschuldigt">Nicht entschuldigt</button>
        <button class="nl-knopf" data-setzen="offen">Offen</button><button class="nl-knopf" data-zusammen>Zusammenfassen</button>
        <a class="nl-knopf ms-auto" href="#/berichte/abwesenheiten/${encodeURIComponent(klasse || '-')}/${von}/${bis}">Berichte</a></div>` : ''}`
      : `<div class="p-3">${leer('Keine Abwesenheiten im Zeitraum.', 'person-check')}</div>`}</div>`;
  const f = n => inhalt.querySelector(`[data-${n}]`)?.value ?? '';
  const sf = suchfeldMitFilterBinden(inhalt, {
    beiSuche: t => { t = t.toLowerCase(); inhalt.querySelectorAll('.nl-tabelle tbody tr').forEach(tr => { tr.hidden = t.length > 0 && !tr.dataset.name.includes(t); }); },
    beiFilter: () => filterNeu(),
  });
  const filterNeu = () => kontext.navigieren(ziel('schueler', f('von') || von, f('bis') || bis, sf.wert('klasse'), sf.wert('status'), sf.wert('grund'), sf.wert('art')));
  inhalt.querySelectorAll('.nl-pille input, .nl-pille select').forEach(x => x.onchange = filterNeu);
  inhalt.querySelectorAll('[data-kz]').forEach(b => b.onclick = () => {
    const kz = b.dataset.kz;
    if (kz === 'offen' || kz === 'unentschuldigt') kontext.navigieren(ziel('schueler', von, bis, klasse, status === kz ? '' : kz, grund, status === kz ? art : 'fehlt'));
    else kontext.navigieren(ziel('schueler', von, bis, klasse, '', grund, art === kz ? '' : kz));
  });
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
  inhalt.querySelector('[data-zusammen]')?.addEventListener('click', async () => {
    const auswahl = gewaehlt();
    if (auswahl.length < 2) return toast('Bitte mindestens zwei Abwesenheiten einer Schülerin bzw. eines Schülers auswählen.', true);
    try { await senden('/api/app/abwesenheiten/zusammenfassen', { ids: auswahl.flatMap(z => z.ids) }); toast('Zusammengefasst.'); neu(); } catch (err) { toast(err.message, true); }
  });
  inhalt.querySelectorAll('[data-loeschen]').forEach(b => b.onclick = async () => {
    const z = liste[Number(b.dataset.loeschen)];
    if (!await bestaetigen('Abwesenheit löschen', `Abwesenheit von ${z.schueler} am ${datumDe(z.datum)}${z.bis !== z.datum ? ' – ' + datumDe(z.bis) : ''} löschen?`)) return;
    await senden('/api/app/abwesenheiten/loeschen', { ids: z.ids });
    toast('Gelöscht.'); neu();
  });
  const zeitfelder = (z = {}) => `
        <div class="col-sm-6"><label class="form-label">Von</label><div class="d-flex gap-2"><input type="date" class="form-control" name="vonDatum" value="${e(z.datum || heuteIso())}"><input type="time" step="60" class="form-control" style="max-width:120px" name="vonZeit" value="${e(z.beginn || (el.stunden ?? [])[0]?.beginn || '07:45')}"></div></div>
        <div class="col-sm-6"><label class="form-label">Bis</label><div class="d-flex gap-2"><input type="date" class="form-control" name="bisDatum" value="${e(z.bis || heuteIso())}"><input type="time" step="60" class="form-control" style="max-width:120px" name="bisZeit" value="${e(z.ende || (el.stunden ?? []).at(-1)?.ende || '16:00')}"></div>
          <div class="form-text">Minutengenau: angebrochene Stunden werden als Verspätung bzw. vorzeitiges Gehen gezählt.</div></div>
        <div class="col-12"><div class="form-check form-switch"><input class="form-check-input" type="checkbox" role="switch" id="abw-ganz" name="ganztaegig" ${z.ganztaegig ? 'checked' : ''}>
          <label class="form-check-label" for="abw-ganz">Ganztägig</label><span class="form-text ms-2">alle Unterrichtsstunden der gewählten Tage (z. B. Krankmeldung vor Unterrichtsbeginn)</span></div></div>`;
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
  inhalt.querySelectorAll('[data-bearbeiten]').forEach(b => b.onclick = () => {
    const z = liste[Number(b.dataset.bearbeiten)];
    dialog({ titel: `Abwesenheit ${z.schueler}`, breit: true, inhalt: `<p class="small text-body-secondary">${z.fehlstunden} Fehlstunden · ${z.fehlminuten} min · ${e(z.faecher.join(', '))}</p>
        <div class="row g-3">${zeitfelder({ ...z, ganztaegig: istGanztaegig(z) })}
        <div class="col-sm-6"><label class="form-label">Abwesenheitsgrund</label><select class="form-select" name="grund"><option value="">–</option>${gruende.map(g => `<option value="${e(g.kurz)}" ${g.kurz === z.grund ? 'selected' : ''}>${e(g.name)}</option>`).join('')}</select></div>
        <div class="col-sm-6"><label class="form-label">Status</label><select class="form-select" name="status">${['offen', 'entschuldigt', 'unentschuldigt'].map(s => `<option value="${s}" ${s === z.status ? 'selected' : ''}>${s === 'unentschuldigt' ? 'nicht entschuldigt' : s}</option>`).join('')}</select></div>
        <div class="col-12"><label class="form-label">Text</label><input class="form-control" name="bemerkung" value="${e(z.bemerkung)}" list="abw-texte">${textListe}</div>
        ${detailFelder(z)}</div>
        ${z.erstellt ? `<p class="small text-body-secondary mt-3 mb-0">Erfasst ${e(z.erstellt.slice(8, 10) + '.' + z.erstellt.slice(5, 7) + '.' + z.erstellt.slice(0, 4) + z.erstellt.slice(10))}${z.lehrer.length ? ` von ${e(z.lehrer.join(', '))}` : ''}</p>` : ''}`,
      beimOeffnen: ganzBinden,
      knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
        const v = n => box.querySelector(`[name=${n}]`).value;
        await senden('/api/app/klassenbuch/abwesenheit', { schuelerIds: [z.schuelerId], ...zeitWerte(box), ersetzen: z.ids, grund: v('grund'), status: v('status'), text: v('bemerkung'), ...detailWerte(box) });
        toast('Gespeichert.'); neu();
      } }] });
  });
  document.querySelector('[data-neu]')?.addEventListener('click', async () => {
    const kl = klasse || el.klassen.find(k => ich.element && (k.leitung ?? []).includes(ich.element))?.kurz || el.klassen[0]?.kurz;
    const schueler = kl ? await holen(`/api/app/klassenbuch/schueler?klasse=${encodeURIComponent(kl)}`) : [];
    dialog({ titel: 'Neue Abwesenheit', breit: true,
      inhalt: `<div class="row g-3">
        <div class="col-sm-4"><label class="form-label">Klasse</label><select class="form-select" name="klasse">${el.klassen.map(k => `<option ${k.kurz === kl ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select></div>
        <div class="col-sm-8"><label class="form-label">Schüler*in</label><select class="form-select" name="schuelerId">${schueler.map(s => `<option value="${s.id}">${e(s.name)} ${e(s.vorname)}</option>`).join('')}</select></div>
        ${zeitfelder()}
        <div class="col-sm-6"><label class="form-label">Abwesenheitsgrund</label><select class="form-select" name="grund"><option value="">–</option>${gruende.map(g => `<option value="${e(g.kurz)}">${e(g.name)}</option>`).join('')}</select></div>
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
      knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
        const v = n => box.querySelector(`[name=${n}]`).value;
        const r = await senden('/api/app/klassenbuch/abwesenheit', { schuelerIds: [Number(v('schuelerId'))], ...zeitWerte(box), grund: v('grund'), status: v('status'), text: v('bemerkung'), ...detailWerte(box) });
        toast(`${r.stunden} ${r.stunden === 1 ? 'Fehlstunde' : 'Fehlstunden'} eingetragen.`); neu();
      } }] });
  });
  document.querySelector('[data-csv]')?.addEventListener('click', () => herunterladen(`abwesenheiten-${von}-${bis}.csv`,
    ['Schüler*in;Klasse;Von;Bis;Fehlstunden;Art;Minuten;Grund;Fach;Status;Text;Nachweis;Entschuldigung am;Eltern informiert;erfasst von', ...liste.map(z => [z.schueler, z.klasse, `${z.datum} ${z.beginn}`, `${z.bis} ${z.ende}`, z.fehlstunden, z.art, z.art === 'fehlt' ? z.fehlminuten : z.minuten, grundName[z.grund] ?? z.grund, z.faecher.join(', '), z.status, z.bemerkung, NACHWEISE[z.nachweis] ?? '', z.entschuldigtAm, z.elternInformiert ? 'ja' : '', z.lehrer.join(', ')]
      .map(x => `"${String(x ?? '').replace(/"/g, '""')}"`).join(';'))].join('\r\n')));
}
