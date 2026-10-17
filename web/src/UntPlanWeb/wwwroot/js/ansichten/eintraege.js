// Klassenbucheinträge: Vermerke (Lob, Ordnungsmaßnahme, Elterngespräch …) über alle oder eine Klasse, mit Filter und neuem Eintrag.
// Klick auf einen Eintrag öffnet den Detaildialog (gemeinsame kbDialog-Komponente) mit allen Angaben und – je nach Recht – Bearbeiten/Löschen.
import { e, icon, api, holen, senden, toast, leer, datumKurz, datumLang, heuteIso, plusTage, suchfeldMitFilter, suchfeldMitFilterBinden, zeitraumPillen, zeilenFiltern } from '../ui.js';
import { kbDialog as dialog, kbBestaetigen as bestaetigen } from '../kbdialog.js';

const FARBE = { Lob: 'gruen', 'Ordnungsmaßnahme': 'rot', 'Elterngespräch': 'orange', 'Hausaufgabe fehlt': 'gelb', 'Material vergessen': 'gelb' };
/** Verfasser*in: Kürzel mit vollem Namen der Lehrkraft (sonst der gespeicherte Name, z. B. „Administration“). */
const verfasser = (k, el) => { const l = el?.lehrer?.find(x => x.kurz === k); return l ? `${l.name} (${l.kurz})` : (k || '–'); };
const zeitstempel = s => { const m = /^(\d{4})-(\d{2})-(\d{2})[ T](\d{2}:\d{2})/.exec(s ?? ''); return m ? `${m[3]}.${m[2]}.${m[1]}, ${m[4]} Uhr` : (s || '–'); };

export async function zeigen(seite, kontext, [vonParam, bisParam, klasseParam, artParam]) {
  const el = await kontext.elemente();
  const iso = /^\d{4}-\d{2}-\d{2}$/;
  const von = iso.test(vonParam ?? '') ? vonParam : plusTage(heuteIso(), -30);
  const bis = iso.test(bisParam ?? '') ? bisParam : heuteIso();
  const klasse = klasseParam && klasseParam !== '-' ? klasseParam : '';
  const art = artParam && artParam !== '-' ? artParam : '';
  kontext.titel('Klassenbucheinträge', `<button class="btn btn-primary" data-neu>${icon('plus')}Neuer Eintrag</button>`);
  const ziel = (v, b, k, a) => `#/eintraege/${v}/${b}/${encodeURIComponent(k || '-')}/${encodeURIComponent(a || '-')}`;
  const q = `von=${von}&bis=${bis}${klasse ? `&klasse=${encodeURIComponent(klasse)}` : ''}${art ? `&art=${encodeURIComponent(art)}` : ''}`;
  const daten = await holen(`/api/app/klassenbucheintraege?${q}`);
  const stundeText = x => x.stunde ? `${x.stunde}. Stunde${x.zeit ? ` · ${x.zeit} Uhr` : ''}` : x.zeit ? `${x.zeit} Uhr` : '';
  seite.innerHTML = `
    <div class="u-sf-leiste u-kopfteil">
      ${suchfeldMitFilter({ platzhalter: 'Schüler*in, Text oder Lehrkraft suchen', label: 'Einträge durchsuchen', filter: [
        { name: 'klasse', label: 'Klasse', alle: 'Alle Klassen', wert: klasse, optionen: el.klassen.map(k => [k.kurz, k.kurz]) },
        { name: 'art', label: 'Art', alle: 'Alle Arten', wert: art, optionen: daten.arten.map(a => [a, a]) },
      ] })}
      ${zeitraumPillen(von, bis)}
    </div>
    <div class="card">${daten.eintraege.length ? `<div class="table-responsive"><table class="table table-hover table-card align-middle u-karten-tabelle ke-tabelle"><thead><tr><th>Datum</th><th>Klasse</th><th>Schüler*in</th><th>Art</th><th>Text</th><th>Lehrkraft</th><th><span class="visually-hidden">Aktionen</span></th></tr></thead><tbody>
      ${daten.eintraege.map((x, i) => `<tr data-eintrag="${i}" tabindex="0" role="button" aria-label="${e(`Eintrag vom ${datumKurz(x.datum)}${x.datum.slice(0, 4)}, ${x.art}, Klasse ${x.klasse} – Details öffnen`)}">
        <td class="text-nowrap" data-label="Datum">${e(datumKurz(x.datum))}${x.datum.slice(0, 4)}${stundeText(x) ? `<div class="ke-stunde">${e(stundeText(x))}</div>` : ''}</td>
        <td class="fw-semibold" data-label="Klasse">${e(x.klasse)}</td><td data-label="Schüler*in">${e(x.schueler || 'ganze Klasse')}</td>
        <td data-label="Art"><span class="u-chip ${FARBE[x.art] ?? ''}">${e(x.art)}</span></td><td class="ke-text" data-label="Text">${e(x.text)}</td><td class="text-body-secondary" data-label="Lehrkraft">${e(x.lehrer)}</td>
        <td class="text-end ke-aktion">${x.loeschen ? `<button class="btn btn-sm btn-link text-danger p-0" data-weg="${x.id}" title="Löschen" aria-label="Löschen">${icon('loeschen')}</button>` : ''}<i class="bi bi-chevron-right ke-pfeil" aria-hidden="true"></i></td></tr>`).join('')}
      </tbody></table></div>` : leer('Keine Klassenbucheinträge im Zeitraum.', 'eintraege')}</div>`;
  const f = n => seite.querySelector(`[data-${n}]`)?.value ?? '';
  const sf = suchfeldMitFilterBinden(seite, {
    beiSuche: t => zeilenFiltern(seite.querySelectorAll('tr[data-eintrag]'), t),
    beiFilter: () => filterNeu(),
  });
  const filterNeu = () => kontext.navigieren(ziel(f('von') || von, f('bis') || bis, sf.wert('klasse'), sf.wert('art')));
  seite.querySelectorAll('[data-von], [data-bis]').forEach(x => x.onchange = filterNeu);
  const neu = () => kontext.navigieren(location.hash);
  const loeschen = async id => {
    if (!await bestaetigen('Eintrag löschen', 'Den Klassenbucheintrag endgültig löschen?')) return false;
    try { await api(`/api/app/klassenbuch/vermerke/${id}`, { methode: 'DELETE' }); toast('Eintrag gelöscht.'); neu(); return true; } catch (err) { toast(err.message, true); return false; }
  };
  seite.querySelectorAll('[data-weg]').forEach(b => b.onclick = ev => { ev.stopPropagation(); loeschen(b.dataset.weg); });
  seite.querySelectorAll('tr[data-eintrag]').forEach(tr => {
    const oeffnen = () => detail(daten.eintraege[Number(tr.dataset.eintrag)], { el, arten: daten.arten, loeschen, fertig: neu, stundeText });
    tr.onclick = ev => { if (!ev.target.closest('[data-weg]')) oeffnen(); };
    tr.onkeydown = ev => { if ((ev.key === 'Enter' || ev.key === ' ') && ev.target === tr) { ev.preventDefault(); oeffnen(); } };
  });
  document.querySelector('[data-neu]').onclick = () => eintragDialog(el, { klasse: klasse || el.klassen[0]?.kurz }, daten.arten, neu);
}

/** Detaildialog eines Klassenbucheintrags: alle Angaben, Aktionen Bearbeiten/Löschen je nach Recht. */
export function detail(x, { el, arten, loeschen, fertig, stundeText }) {
  const zeile = (titel, wert, klasse = '') => `<div class="ke-zeile ${klasse}"><dt>${e(titel)}</dt><dd>${wert}</dd></div>`;
  const knoepfe = [];
  if (x.loeschen) knoepfe.push({ text: `${icon('loeschen')}Löschen`, art: 'gefahr', aktion: async () => ((await loeschen(x.id)) ? undefined : false) });
  knoepfe.push({ text: 'Schließen' });
  if (x.bearbeiten) knoepfe.push({ text: `${icon('stift')}Bearbeiten`, art: 'haupt', aktion: () => { eintragDialog(el, x, arten, fertig); } });
  return dialog({
    titel: 'Klassenbucheintrag',
    breite: 'm',
    klasse: 'ke-detail',
    inhalt: `<div class="ke-kopf"><span class="u-chip ${FARBE[x.art] ?? ''}">${e(x.art)}</span><span class="ke-kopf-klasse">Klasse ${e(x.klasse)}</span></div>
      <p class="ke-volltext">${e(x.text)}</p>
      <dl class="ke-angaben">
        ${zeile('Datum', e(datumLang(x.datum)))}
        ${zeile('Stunde', e(stundeText(x) || '–'))}
        ${zeile('Klasse', e(x.klasse))}
        ${zeile('Schüler*in', e(x.schueler || 'ganze Klasse'))}
        ${zeile('Kategorie', e(x.art))}
        ${zeile('Verfasser*in', e(verfasser(x.lehrer, el)))}
        ${zeile('Erfasst', e(zeitstempel(x.erstellt)))}
      </dl>
      ${!x.bearbeiten ? '<p class="ke-hinweis"><i class="bi bi-lock" aria-hidden="true"></i>Bearbeiten können die Verfasserin bzw. der Verfasser und die Klassenleitung.</p>' : ''}`,
    knoepfe,
  });
}

/** Neuer Eintrag (x.id fehlt) bzw. bestehenden Eintrag bearbeiten (Klasse bleibt fest). */
async function eintragDialog(el, x, arten, fertig) {
  const bearbeiten = !!x.id;
  const laden = k => holen(`/api/app/klassenbuch/schueler?klasse=${encodeURIComponent(k)}`).catch(() => []);
  const schueler = await laden(x.klasse);
  const optionen = (l, wahl) => `<option value="">ganze Klasse</option>${l.map(s => `<option value="${s.id}" ${s.id === wahl ? 'selected' : ''}>${e(s.name)}, ${e(s.vorname)}</option>`).join('')}`;
  dialog({
    titel: bearbeiten ? 'Klassenbucheintrag bearbeiten' : 'Neuer Klassenbucheintrag',
    inhalt: `<div class="row g-3">
      <div class="col-sm-6"><label class="form-label">Klasse</label><select class="form-select" name="klasse" ${bearbeiten ? 'disabled' : ''}>${el.klassen.map(k => `<option ${k.kurz === x.klasse ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select></div>
      <div class="col-sm-6"><label class="form-label">Datum</label><input type="date" class="form-control" name="datum" value="${x.datum ?? heuteIso()}"></div>
      <div class="col-sm-6"><label class="form-label">Art</label><select class="form-select" name="art">${arten.map(a => `<option ${a === x.art ? 'selected' : ''}>${e(a)}</option>`).join('')}</select></div>
      <div class="col-sm-6"><label class="form-label">Betrifft</label><select class="form-select" name="schueler">${optionen(schueler, x.schuelerId)}</select></div>
      <div class="col-12"><label class="form-label">Text</label><textarea class="form-control" name="text" rows="3" maxlength="1000" required>${e(x.text ?? '')}</textarea></div></div>`,
    beimOeffnen: box => {
      if (!bearbeiten) box.querySelector('[name=klasse]').onchange = async ev => { box.querySelector('[name=schueler]').innerHTML = optionen(await laden(ev.target.value)); };
    },
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
      const f = n => box.querySelector(`[name=${n}]`).value;
      const d = { datum: f('datum'), klasse: bearbeiten ? x.klasse : f('klasse'), schuelerId: f('schueler') ? Number(f('schueler')) : null, art: f('art'), text: f('text') };
      if (bearbeiten) await senden(`/api/app/klassenbuch/vermerke/${x.id}`, { ...d, zeit: x.zeit || null }, 'PUT');
      else await senden('/api/app/klassenbuch/vermerke', d);
      toast('Eintrag gespeichert.');
      fertig();
    } }],
  });
}
