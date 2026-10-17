// Klassenbuch → Fehlzeiten (Fachlehrkraft): Fachlehrer-Entschuldigungen im modernen Design. Lehrkräfte sehen die Fehlstunden aus ihrem
// eigenen Unterricht (Klassenfilter mit den relevanten Klassen), entschuldigen einzelne bzw. mehrere Stunden (Checkboxen) mit Status, Datum
// und Text; der Text ist für andere Lehrkräfte derselben Abwesenheit sichtbar. Ein einmal gesetzter Status ist gesperrt (der Dialog zeigt
// dann den Text der Klassenleitung bzw. der Lehrkraft). Dazu die Seitenleiste „Fehlzeiten“ einer Abwesenheit und der Entschuldigungsdialog
// der Klassenleitung (Abwesenheiten) – beide auch für andere Seiten exportiert.
import { e, holen, senden, toast, leer, heuteIso, plusTage, infoHinweis, suchfeldMitFilter, suchfeldMitFilterBinden, zeilenFiltern } from '../ui.js';
import { kbDialog } from '../kbdialog.js';

const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';
const WT = ['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'];
const wtKurz = iso => WT[new Date(`${iso}T12:00:00`).getDay()] ?? '';
const TAGE = ['Sonntag', 'Montag', 'Dienstag', 'Mittwoch', 'Donnerstag', 'Freitag', 'Samstag'];
const tagLang = iso => `${TAGE[new Date(`${iso}T12:00:00`).getDay()]}, ${datumDe(iso)}`;
const zeitDe = z => z ? `${datumDe(z.slice(0, 10))} ${z.slice(11, 16)}` : '';

/** Status als Chip (offen gelb, entschuldigt grün, nicht entschuldigt rot). */
export const statusChip = st => st === 'entschuldigt' ? '<span class="u-chip gruen">entschuldigt</span>'
  : st === 'unentschuldigt' ? '<span class="u-chip rot">nicht entschuldigt</span>' : '<span class="u-chip gelb">offen</span>';

/** Zuletzt eingegebene Entschuldigungstexte derselben Abwesenheit. */
const texteHtml = texte => texte?.length ? `<div class="fe-texte"><h3>Zuletzt eingegebene Entschuldigungstexte</h3>${texte.map(t => `<div class="fe-text">
    <div class="fe-wer"><b>${e(t.lehrer || '–')}</b>${t.rolle === 'klassenleitung' ? ' (Klassenleitung)' : ''} · ${e(zeitDe(t.zeit))}${t.fach ? ` · ${e(t.fach)}, ${t.stunde}. Std.` : ''} · ${t.status === 'unentschuldigt' ? 'nicht entschuldigt' : e(t.status)}</div>
    <div class="fe-inhalt">${e(t.text)}</div></div>`).join('')}</div>` : '';

/**
 * Entschuldigungsdialog der Fachlehrkraft für eine oder mehrere Fehlstunden (Zeilen aus /api/app/entschuldigungen/fehlzeiten).
 * Gesperrte Stunde: nur lesen (Text der Klassenleitung bzw. der Lehrkraft), alle Aktionen gesperrt. fertig(): nach dem Speichern.
 */
export function stundenDialog(zeilen, fertig = () => {}) {
  const z = zeilen[0];
  const gesperrt = zeilen.length === 1 && z.gesperrt;
  const titel = zeilen.length === 1 ? `Entschuldigung für ${z.schueler}` : `Entschuldigung für ${zeilen.length} Fehlstunden`;
  const ent = z.entschuldigung;
  const abw = zeilen.length === 1
    ? `${e(z.schueler)}, ${e(z.fach)}, ${datumDe(z.datum)}, ${e(z.beginn)} – ${e(z.ende)}`
    : `<ul class="mb-0 ps-3">${zeilen.slice(0, 8).map(x => `<li>${e(x.schueler)}, ${e(x.fach)}, ${datumDe(x.datum)}, ${e(x.beginn)} – ${e(x.ende)}</li>`).join('')}${zeilen.length > 8 ? `<li>… und ${zeilen.length - 8} weitere</li>` : ''}</ul>`;
  const lesetext = ent?.text || (gesperrt ? z.abwesenheitText : '');
  const texte = zeilen.length === 1 ? z.texte : [];
  kbDialog({
    titel, breite: 's',
    inhalt: `${gesperrt ? `<div class="fe-gesperrt"><i class="bi bi-lock" aria-hidden="true"></i><span>${e(z.hinweis)}</span></div>` : ''}
      <div class="row g-3">
        <div class="col-6"><label class="form-label" for="fe-datum">Datum</label><input type="date" class="form-control" id="fe-datum" name="datum" value="${e(ent?.datum || heuteIso())}" ${gesperrt ? 'disabled' : ''}></div>
        <div class="col-6"><label class="form-label" for="fe-status">Status</label><select class="form-select" id="fe-status" name="status" ${gesperrt ? 'disabled' : ''}>
          ${gesperrt ? `<option>${z.status === 'unentschuldigt' ? 'nicht entschuldigt' : e(z.status)}</option>` : '<option value="entschuldigt">entschuldigt</option><option value="unentschuldigt">nicht entschuldigt</option>'}</select></div>
        <div class="col-12"><div class="form-label mb-1">Abwesenheit</div><div class="small text-body-secondary">${abw}</div></div>
        <div class="col-12"><label class="form-label" for="fe-text">Text</label><textarea class="form-control" id="fe-text" name="text" rows="3" maxlength="1000" ${gesperrt ? 'disabled' : ''}
          placeholder="z. B. Attest liegt vor">${e(gesperrt ? lesetext : '')}</textarea>
          ${gesperrt ? '' : '<div class="form-text">Der Text ist für andere Lehrkräfte mit einer Fehlstunde in derselben Abwesenheit sichtbar.</div>'}</div>
      </div>${texteHtml(texte)}`,
    knoepfe: gesperrt ? [{ text: 'Schließen' }] : [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
      const v = n => box.querySelector(`[name=${n}]`).value;
      const r = await senden('/api/app/entschuldigungen/fehlzeiten', { ids: zeilen.map(x => x.id), status: v('status'), text: v('text'), datum: v('datum') || null });
      toast(r.gesperrt?.length ? `${r.geaendert} gespeichert, ${r.gesperrt.length} bereits entschuldigt (gesperrt).` : `${r.geaendert} ${r.geaendert === 1 ? 'Fehlstunde' : 'Fehlstunden'} gespeichert.`);
      fertig();
    } }],
  });
}

/** Seitenleiste „Fehlzeiten“ einer Abwesenheit: Schüler*in, Beginn, Ende, Grund und je Stunde Zeit, Fach, Lehrkraft, Status, Text. */
export function seitenleiste(a, grundName = g => g) {
  document.querySelector('.fe-seite')?.remove();
  const s = document.createElement('aside');
  s.className = 'fe-seite';
  s.setAttribute('role', 'dialog');
  s.setAttribute('aria-modal', 'false');
  s.setAttribute('aria-labelledby', 'fe-seite-titel');
  const tage = [...new Set(a.stunden.map(x => x.datum))];
  s.innerHTML = `<header class="fe-seite-kopf"><h2 id="fe-seite-titel">Fehlzeiten</h2><button type="button" class="kbd-zu" data-zu title="Schließen" aria-label="Schließen"><i class="bi bi-x-lg" aria-hidden="true"></i></button></header>
    <div class="fe-seite-rumpf">
      <dl><dt>${e(a.schueler)}</dt><dd>${e(a.klasse)}</dd><dt>Beginn</dt><dd>${datumDe(a.vonDatum)} ${e(a.vonZeit)}</dd><dt>Ende</dt><dd>${datumDe(a.bisDatum)} ${e(a.bisZeit)}</dd>
        ${a.grund ? `<dt>Abwesenheitsgrund</dt><dd>${e(grundName(a.grund))}</dd>` : ''}<dt>Status</dt><dd>${statusChip(a.status)}${a.mehrdeutig ? ' <span class="fe-mehrdeutig">(mehrdeutig)</span>' : ''}</dd>
        <dt>Bearbeitet</dt><dd>${a.lehrerBearbeitet}/${a.lehrerGesamt} Lehrkräfte</dd></dl>
      ${a.mehrdeutig ? '<p class="fe-warnung"><i class="bi bi-exclamation-triangle" aria-hidden="true"></i><span>Die Stunden haben unterschiedliche Status – bitte die Abwesenheit prüfen und als Ganzes entschuldigen.</span></p>' : ''}
      ${tage.map(t => `<div class="fe-tag">${e(tagLang(t))}</div>${a.stunden.filter(x => x.datum === t).sort((x, y) => y.stunde - x.stunde).map(x => `<div class="fe-stunde">
        <div class="fe-stunde-kopf"><span>${e(x.beginn)} – ${e(x.ende)}</span>${statusChip(x.status)}</div>
        <div class="fe-stunde-unter">${e(x.fach)}${x.lehrer ? ` – ${e(x.lehrer)}` : ''}${x.entschuldigung ? ` · ${x.entschuldigung.rolle === 'klassenleitung' ? 'Klassenleitung' : 'Fachlehrkraft'} ${e(x.entschuldigung.lehrer)}, ${e(zeitDe(x.entschuldigung.zeit))}` : ''}</div>
        ${x.entschuldigung?.text ? `<div class="fe-stunde-text">${e(x.entschuldigung.text)}</div>` : ''}</div>`).join('')}`).join('')}
    </div>`;
  document.body.append(s);
  const zu = () => { s.classList.remove('an'); setTimeout(() => s.remove(), 180); document.removeEventListener('keydown', taste, true); };
  const taste = ev => { if (ev.key === 'Escape' && !document.querySelector('.kbd-overlay')) { ev.stopPropagation(); zu(); } };
  document.addEventListener('keydown', taste, true);
  window.addEventListener('hashchange', zu, { once: true });
  s.querySelector('[data-zu]').onclick = zu;
  requestAnimationFrame(() => s.classList.add('an'));
  s.querySelector('[data-zu]').focus();
  return s;
}

/**
 * Entschuldigungsdialog der Klassenleitung für ganze Abwesenheiten (überschreibt die Status der Fachlehrkräfte – mit Warnung).
 * abwesenheiten: Einträge aus /api/app/entschuldigungen/abwesenheiten; status: Vorgabe.
 */
export function abwesenheitDialog(abwesenheiten, { status = 'entschuldigt', nachweise = {}, fertig = () => {} } = {}) {
  const ids = abwesenheiten.flatMap(a => a.ids);
  const ueber = abwesenheiten.some(a => a.stunden.some(x => x.entschuldigung?.rolle === 'fachlehrer'));
  const a = abwesenheiten[0];
  const kl = abwesenheiten.flatMap(x => x.stunden).map(x => x.entschuldigung).find(x => x?.rolle === 'klassenleitung');
  kbDialog({
    titel: abwesenheiten.length === 1 ? `Entschuldigung für ${a.schueler}` : `Entschuldigung für ${abwesenheiten.length} Abwesenheiten`, breite: 'm',
    inhalt: `${ueber ? '<p class="fe-warnung"><i class="bi bi-exclamation-triangle" aria-hidden="true"></i><span><b>Warnung:</b> Der Entschuldigungsstatus der Fehlstunden wird überschrieben (Fachlehrkräfte haben einzelne Stunden bereits bearbeitet).</span></p>' : ''}
      <div class="row g-3">
        <div class="col-sm-6"><label class="form-label" for="fk-status">Status</label><select class="form-select" id="fk-status" name="status">
          ${[['entschuldigt', 'entschuldigt'], ['unentschuldigt', 'nicht entschuldigt'], ['offen', 'offen (zurücknehmen)']].map(([w, t]) => `<option value="${w}" ${w === status ? 'selected' : ''}>${t}</option>`).join('')}</select></div>
        <div class="col-sm-6"><label class="form-label" for="fk-datum">Datum</label><input type="date" class="form-control" id="fk-datum" name="datum" value="${e(a.entschuldigtAm || heuteIso())}"></div>
        <div class="col-12"><div class="form-label mb-1">Abwesenheit</div><div class="small text-body-secondary">${abwesenheiten.slice(0, 6).map(x => `${e(x.schueler)}, ${datumDe(x.vonDatum)} ${e(x.vonZeit)} – ${datumDe(x.bisDatum)} ${e(x.bisZeit)}`).join('<br>')}${abwesenheiten.length > 6 ? `<br>… und ${abwesenheiten.length - 6} weitere` : ''}</div></div>
        <div class="col-sm-6"><label class="form-label" for="fk-nachweis">Nachweis</label><select class="form-select" id="fk-nachweis" name="nachweis"><option value="">kein Nachweis</option>${Object.entries(nachweise).map(([k, t]) => `<option value="${k}" ${k === a.nachweis ? 'selected' : ''}>${e(t)}</option>`).join('')}</select></div>
        <div class="col-12"><label class="form-label" for="fk-text">Text</label><textarea class="form-control" id="fk-text" name="text" rows="3" maxlength="1000" placeholder="z. B. Abwesenheit ist OK.">${e(kl?.text ?? '')}</textarea>
          <div class="form-text">Fachlehrkräfte sehen den Text, wenn sie eine Stunde dieser Abwesenheit öffnen.</div></div>
      </div>`,
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
      const v = n => box.querySelector(`[name=${n}]`).value;
      const st = v('status');
      const r = st === 'offen'
        ? await senden('/api/app/entschuldigungen/zuruecknehmen', { ids })
        : await senden('/api/app/entschuldigungen/abwesenheit', { ids, status: st, text: v('text'), datum: v('datum') || null, nachweis: v('nachweis') });
      toast(st === 'offen' ? 'Entschuldigung zurückgenommen.' : r.ueberschrieben ? `Gespeichert – ${r.ueberschrieben} ${r.ueberschrieben === 1 ? 'Stunde' : 'Stunden'} der Fachlehrkräfte überschrieben.` : 'Gespeichert.');
      fertig();
    } }],
  });
}

export async function zeigen(seite, kontext, [vonParam, bisParam, klasseParam, statusParam, offenParam]) {
  const iso = /^\d{4}-\d{2}-\d{2}$/;
  const von = iso.test(vonParam ?? '') ? vonParam : plusTage(heuteIso(), -14);
  const bis = iso.test(bisParam ?? '') ? bisParam : heuteIso();
  const klasse = klasseParam && klasseParam !== '-' ? decodeURIComponent(klasseParam) : '';
  const status = ['offen', 'entschuldigt', 'unentschuldigt'].includes(statusParam) ? statusParam : '';
  const nurOffen = offenParam === '1';
  const ziel = (v = von, b = bis, k = klasse, s = status, o = nurOffen) => `#/entschuldigen/${v}/${b}/${encodeURIComponent(k || '-')}/${s || '-'}/${o ? 1 : 0}`;
  kontext.titel('Fehlzeiten');
  const [d, gruende] = await Promise.all([
    holen(`/api/app/entschuldigungen/fehlzeiten?von=${von}&bis=${bis}${klasse ? `&klasse=${encodeURIComponent(klasse)}` : ''}${status ? `&status=${status}` : ''}${nurOffen ? '&nurOffene=true' : ''}`),
    holen('/api/app/abwesenheitsgruende?art=abwesenheit').catch(() => []),
  ]);
  const grundName = Object.fromEntries(gruende.map(g => [g.kurz, g.name]));
  const liste = d.zeilen;
  kontext.titel('Fehlzeiten', '', String(liste.length));
  const pille = (name, html, aktiv) => `<label class="nl-pille ${aktiv ? 'aktiv' : ''}"><span class="visually-hidden">${name}</span>${html}</label>`;
  const waehlbar = liste.some(z => z.darf);
  seite.innerHTML = `
    <div class="nl-leiste u-kopfteil abw-leiste">
      ${suchfeldMitFilter({ platzhalter: 'Schüler*in oder Fach suchen', label: 'Fehlstunden durchsuchen', filter: [
        { name: 'klasse', label: 'Klasse', alle: 'Alle Klassen', wert: klasse, optionen: d.klassen.map(k => [k, k]) },
        { name: 'status', label: 'Status', alle: 'Jeder Status', wert: status, optionen: [['offen', 'offen'], ['entschuldigt', 'entschuldigt'], ['unentschuldigt', 'nicht entschuldigt']] },
      ] })}
      ${pille('Von', `<input type="date" data-von value="${von}">`, false)}
      ${pille('Bis', `<input type="date" data-bis value="${bis}">`, false)}
      <label class="nl-pille ${nurOffen ? 'aktiv' : ''}"><input type="checkbox" class="me-2" data-offen ${nurOffen ? 'checked' : ''}>Nur offene Fehlstunden</label>
      ${infoHinweis('Hier stehen die Fehlstunden aus <b>Ihrem Unterricht</b>. Sie können einzelne Stunden – auch mehrere auf einmal über die Auswahlkreise – entschuldigen. Ein einmal gesetzter Status kann nur noch von der Klassenleitung geändert werden. Ihr Text ist für die anderen Lehrkräfte derselben Abwesenheit sichtbar.')}
    </div>
    ${d.darf ? '' : `<p class="nl-hinweis"><i class="bi bi-lock"></i>${liste.length ? 'Sie sehen die Fehlstunden Ihres Unterrichts. Entschuldigen ist für Ihr Konto nicht freigegeben bzw. die Fachlehrer-Entschuldigungen sind nicht eingeschaltet.' : 'Die Fachlehrer-Entschuldigungen sind nicht eingeschaltet bzw. für Ihr Konto nicht freigegeben.'}</p>`}
    <div class="nl-tabelle">${liste.length ? `<table><thead><tr>${waehlbar ? '<th class="nl-wahl"><input type="checkbox" class="nl-rund" data-alle aria-label="Alle auswählen"></th>' : ''}
        <th>Schüler*in</th><th>Klasse</th><th>Datum</th><th>Zeit</th><th>Fach</th><th>Lehrkraft</th><th>Abwesenheitsgrund</th><th>Status</th><th>Text</th></tr></thead><tbody>
      ${liste.map((z, i) => `<tr>${waehlbar ? `<td class="nl-wahl">${z.darf ? `<input type="checkbox" class="nl-rund" data-i="${i}" aria-label="auswählen">` : ''}</td>` : ''}
        <td><div class="nl-name">${e(z.schueler)}</div></td><td>${e(z.klasse)}</td>
        <td class="text-nowrap"><span class="fz-grau">${e(wtKurz(z.datum))}</span> ${datumDe(z.datum)}</td><td class="text-nowrap">${e(z.beginn)} – ${e(z.ende)}</td>
        <td>${e(z.fach)}</td><td>${e(z.lehrer)}</td><td>${z.grund ? `<span class="nl-tag">${e(grundName[z.grund] ?? z.grund)}</span>` : ''}</td>
        <td><button type="button" class="nl-knopf ${z.darf ? '' : 'fe-lesen'}" data-oeffnen="${i}" title="${z.darf ? 'Entschuldigen' : e(z.hinweis || 'Ansehen')}">${z.darf ? '<i class="bi bi-question-circle" aria-hidden="true"></i>entschuldigen' : `${z.gesperrt ? '<i class="bi bi-lock" aria-hidden="true"></i>' : ''}${statusChip(z.status)}`}</button></td>
        <td class="nl-text">${e(z.entschuldigung?.text ?? '')}</td></tr>`).join('')}
      </tbody></table>
      ${waehlbar ? '<div class="nl-fuss"><span class="nl-unter me-2">Ausgewählte Fehlstunden:</span><button type="button" class="nl-knopf gruen" data-entschuldigen>Entschuldigen</button></div>' : ''}`
      : `<div class="p-3">${leer('Keine Fehlstunden in Ihrem Unterricht im Zeitraum.', 'person-check')}</div>`}</div>`;
  const f = n => seite.querySelector(`[data-${n}]`)?.value ?? '';
  const sf = suchfeldMitFilterBinden(seite, {
    beiSuche: t => zeilenFiltern(seite.querySelectorAll('.nl-tabelle tbody tr'), t),
    beiFilter: () => filterNeu(),
  });
  const filterNeu = () => kontext.navigieren(ziel(f('von') || von, f('bis') || bis, sf.wert('klasse'), sf.wert('status')));
  seite.querySelectorAll('.nl-pille input[type=date]').forEach(x => x.onchange = filterNeu);
  seite.querySelector('[data-offen]').onchange = ev => kontext.navigieren(ziel(von, bis, klasse, status, ev.target.checked));
  seite.querySelector('[data-alle]')?.addEventListener('change', ev => seite.querySelectorAll('[data-i]').forEach(c => { c.checked = ev.target.checked; }));
  const neu = () => kontext.navigieren(location.hash);
  seite.querySelectorAll('[data-oeffnen]').forEach(b => b.onclick = () => stundenDialog([liste[Number(b.dataset.oeffnen)]], neu));
  seite.querySelector('[data-entschuldigen]')?.addEventListener('click', () => {
    const auswahl = [...seite.querySelectorAll('[data-i]:checked')].map(c => liste[Number(c.dataset.i)]);
    if (!auswahl.length) return toast('Bitte zuerst Fehlstunden auswählen.', true);
    stundenDialog(auswahl, neu);
  });
}
