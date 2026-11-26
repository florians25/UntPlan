// Klassenbucheinträge: Vermerke (Lob, Ordnungsmaßnahme, Elterngespräch …) über alle oder eine Klasse, mit Filter und neuem Eintrag.
// Klick auf einen Eintrag öffnet den Detaildialog (gemeinsame kbDialog-Komponente) mit allen Angaben und – je nach Recht – Bearbeiten/Löschen.
import { e, icon, api, holen, senden, toast, leer, datumKurz, datumLang, heuteIso, plusTage, suchfeldMitFilter, suchfeldMitFilterBinden, zeitraumPillen, zeilenFiltern, herunterladen, klassenFuer } from '../ui.js';
import { kbDialog as dialog, kbBestaetigen as bestaetigen } from '../kbdialog.js';
import { zeitbereichFeld, zeitbereichBinden } from '../zeitbereich.js';

const FARBE = { Lob: 'gruen', 'Ordnungsmaßnahme': 'rot', 'Elterngespräch': 'orange', 'Hausaufgabe fehlt': 'gelb', 'Material vergessen': 'gelb' };
/** Verfasser*in: Kürzel mit vollem Namen der Lehrkraft (sonst der gespeicherte Name, z. B. „Administration“). */
const verfasser = (k, el) => { const l = el?.lehrer?.find(x => x.kurz === k); return l ? `${l.name} (${l.kurz})` : (k || '–'); };
const zeitstempel = s => { const m = /^(\d{4})-(\d{2})-(\d{2})[ T](\d{2}:\d{2})/.exec(s ?? ''); return m ? `${m[3]}.${m[2]}.${m[1]}, ${m[4]} Uhr` : (s || '–'); };

/** Kategoriegruppen (Eintragungskategorien je Gruppe), z. B. „Maßnahme“: Erzieherische Maßnahme, Ordnungsmaßnahme, Trainingsraum, Konsequenzmodell. */
const KATEGORIEGRUPPEN = { 'Maßnahme': ['Erzieherische Maßnahme', 'Ordnungsmaßnahme', 'Trainingsraum', 'Konsequenzmodell'] };

export async function zeigen(seite, kontext, [vonParam, bisParam, klasseParam, artParam, schuelerParam, gruppeParam]) {
  const el = klassenFuer(await kontext.elemente(), kontext.ich, 'einsehen');
  const iso = /^\d{4}-\d{2}-\d{2}$/;
  const von = iso.test(vonParam ?? '') ? vonParam : plusTage(heuteIso(), -30);
  const bis = iso.test(bisParam ?? '') ? bisParam : heuteIso();
  const klasse = klasseParam && klasseParam !== '-' ? klasseParam : '';
  const art = artParam && artParam !== '-' ? artParam : '';
  const schueler = klasse && schuelerParam && schuelerParam !== '-' ? schuelerParam : '';
  const gruppe = KATEGORIEGRUPPEN[gruppeParam] ? gruppeParam : '';
  // „Bericht“: PDF der Einträge (z. B. einer Schülerin bzw. eines Schülers mit Kategorie „Trainingsraum“) zur Dokumentation oder Beratung
  kontext.titel('Klassenbucheinträge');
  const ziel = (v, b, k, a, sc = '', g = '') => `#/eintraege/${v}/${b}/${encodeURIComponent(k || '-')}/${encodeURIComponent(a || '-')}${sc || g ? `/${sc || '-'}/${encodeURIComponent(g || '-')}` : ''}`;
  const q = `von=${von}&bis=${bis}${klasse ? `&klasse=${encodeURIComponent(klasse)}` : ''}${art ? `&art=${encodeURIComponent(art)}` : ''}`;
  const [daten, kinder] = await Promise.all([holen(`/api/app/klassenbucheintraege?${q}`),
    klasse ? holen(`/api/app/klassenbuch/schueler?klasse=${encodeURIComponent(klasse)}`).catch(() => []) : Promise.resolve([])]);
  // Schüler*in und Kategoriegruppe filtern die geladenen Einträge
  daten.eintraege = daten.eintraege.filter(x => (!schueler || String(x.schuelerId) === schueler) && (!gruppe || KATEGORIEGRUPPEN[gruppe].includes(x.art)));
  const stundeText = x => x.stunde ? `${x.stunde}. Stunde${x.zeit ? `, ${x.zeit} Uhr` : ''}` : x.zeit ? `${x.zeit} Uhr` : '';
  // wie im Original: Klasse | Schüler*innen | Eintragskategorie | Eintragskategoriegruppe | Zeitraum; Tabelle Auswahl | Stift/X | Art | Name | Klasse |
  // Tag | Datum | Zeit | Fach | Benutzer | Eintragskategorie | Kategoriegruppe | Text; darunter Neu | Löschen | Bericht | Berichte
  const sj = kontext.sitzung?.schuljahr ?? kontext.schuljahr ?? {};
  const gruppeVon = a => Object.keys(KATEGORIEGRUPPEN).find(g => KATEGORIEGRUPPEN[g].includes(a)) ?? '';
  const WT = ['So.', 'Mo.', 'Di.', 'Mi.', 'Do.', 'Fr.', 'Sa.'];
  const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';
  const darf = daten.eintraege.some(x => x.loeschen);
  seite.innerHTML = `
    <div class="nl-leiste sg-leiste u-kopfteil ke-leiste">
      <label class="sg-feld"><span>Klasse</span><select class="form-select" data-klasse><option value="">- Alle -</option>${el.klassen.map(k => `<option ${k.kurz === klasse ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select></label>
      <label class="sg-feld"><span>Schüler*innen</span><select class="form-select" data-schueler ${klasse ? '' : 'disabled'}><option value="">- Alle -</option>${kinder.map(k => `<option value="${k.id}" ${String(k.id) === schueler ? 'selected' : ''}>${e(k.name)} ${e(k.vorname)}</option>`).join('')}</select></label>
      <label class="sg-feld"><span>Eintragskategorie</span><select class="form-select" data-art><option value="">- Alle -</option>${daten.arten.map(a => `<option ${a === art ? 'selected' : ''}>${e(a)}</option>`).join('')}</select></label>
      <label class="sg-feld"><span>Eintragskategoriegruppe</span><select class="form-select" data-gruppe><option value="">- Alle -</option>${Object.keys(KATEGORIEGRUPPEN).map(g => `<option ${g === gruppe ? 'selected' : ''}>${e(g)}</option>`).join('')}</select></label>
      ${zeitbereichFeld(von, bis, sj)}
      <span class="sg-feld"><span>Suche</span>${suchfeldMitFilter({ platzhalter: 'Suchen', label: 'Einträge durchsuchen', klasse: 'abw-suche' })}</span>
      <div class="u-fl-aktionen kl-knoepfe">
        <button type="button" class="nl-knopf" data-neu>Neu</button>
        ${darf ? '<button type="button" class="nl-knopf" data-sammel-loeschen>Löschen</button>' : ''}
        <button type="button" class="nl-knopf" data-bericht ${klasse ? '' : 'disabled title="Bitte eine Klasse wählen"'}>Bericht</button>
        <details class="kl-menue"><summary class="nl-knopf">Berichte <i class="bi bi-caret-down-fill"></i></summary>
          <div class="kl-menue-liste"><a href="#/berichte/klassenbucheintraege/${encodeURIComponent(klasse || '-')}/${von}/${bis}">Klassenbucheinträge pro Klasse</a>
            <a href="#/berichte/klassenbucheintraege_schueler/${encodeURIComponent(klasse || '-')}/${von}/${bis}">Klassenbucheinträge pro Schüler*in</a></div></details>
      </div>
    </div>
    <div class="nl-tabelle ke-liste">${daten.eintraege.length ? `<table><thead><tr>${darf ? '<th class="nl-wahl"><span class="sg-kopfwahl">Auswahl<input type="checkbox" class="nl-rund" data-alle aria-label="Alle auswählen"></span></th>' : ''}<th></th>
        <th>Art</th><th>Name</th><th>Klasse</th><th>Tag</th><th>Datum</th><th>Zeit</th><th>Fach</th><th>Benutzer</th><th>Eintragskategorie</th><th>Kategoriegruppe</th><th>Text</th></tr></thead><tbody>
      ${daten.eintraege.map((x, i) => `<tr data-eintrag="${i}" tabindex="0" role="button" aria-label="${e(`Eintrag vom ${datumKurz(x.datum)}${x.datum.slice(0, 4)}, ${x.art}, Klasse ${x.klasse}, Details öffnen`)}">
        ${darf ? `<td class="nl-wahl">${x.loeschen ? `<input type="checkbox" class="nl-rund" data-wahl="${x.id}" aria-label="auswählen">` : ''}</td>` : ''}
        <td class="kl-zelle-icons">${x.bearbeiten ? `<button type="button" class="nl-icon sichtbar" data-stift="${i}" title="Bearbeiten" aria-label="Bearbeiten">${icon('stift')}</button>` : ''}${x.loeschen ? `<button type="button" class="nl-icon sichtbar rot" data-weg="${x.id}" title="Löschen" aria-label="Löschen"><i class="bi bi-x-lg"></i></button>` : ''}</td>
        <td><i class="bi bi-${x.schuelerId ? 'person' : 'people'} nl-unter" title="${x.schuelerId ? 'Schüler*in' : 'Klasse'}"></i></td>
        <td class="text-nowrap">${e(x.schueler || '')}</td><td>${e(x.klasse)}</td><td>${WT[new Date(`${x.datum}T12:00`).getDay()]}</td><td class="text-nowrap">${datumDe(x.datum)}</td>
        <td>${e(x.zeit || '')}</td><td>${e(x.fach || '')}</td><td>${e(x.lehrer)}</td><td>${e(x.art)}</td><td>${e(gruppeVon(x.art))}</td>
        <td><div class="ke-text" title="${e(x.text)}">${e(x.text)}</div></td></tr>`).join('')}
      </tbody></table>
      <div class="sg-fuss"><span>${daten.eintraege.length === 1 ? 'Ein Element gefunden.' : `${daten.eintraege.length} Elemente gefunden, Anzeige aller Elemente.`}</span></div>` : leer('Nichts anzuzeigen')}</div>`;
  const f = n => seite.querySelector(`[data-${n}]`)?.value ?? '';
  suchfeldMitFilterBinden(seite, { beiSuche: t => zeilenFiltern(seite.querySelectorAll('tr[data-eintrag]'), t) });
  const filterNeu = (v = f('von') || von, b = f('bis') || bis) => { const k = f('klasse'); kontext.navigieren(ziel(v, b, k, f('art'), k === klasse ? (f('schueler') || '') : '', f('gruppe'))); };
  ['klasse', 'schueler', 'art', 'gruppe'].forEach(n => { seite.querySelector(`[data-${n}]`).onchange = () => filterNeu(); });
  zeitbereichBinden(seite, sj, filterNeu);
  document.querySelector('[data-bericht]')?.addEventListener('click', async ev => {
    const knopf = ev.currentTarget;
    const kat = art || (gruppe ? `gruppe:${gruppe}` : '');
    const p = new URLSearchParams({ klasse, von, bis, format: 'pdf', ...(schueler ? { schueler } : {}), ...(kat ? { kategorie: kat } : {}) });
    knopf.disabled = true;
    try {
      const r = await fetch(`/api/app/berichte/erstellen/klassenbucheintraege_schueler?${p}`, { credentials: 'same-origin', headers: { 'X-UntPlanWeb': '1' } });
      if (!r.ok) throw new Error((await r.json().catch(() => null))?.fehler ?? `Fehler ${r.status}`);
      herunterladen(`klassenbucheintraege-${klasse}${schueler ? `-${schueler}` : ''}-${von}-${bis}.pdf`, await r.blob());
    } catch (err) { toast(err.message, true); } finally { knopf.disabled = false; }
  });
  // nach dem Speichern: liegt der Eintrag außerhalb von Zeitraum bzw. Filter (z. B. Datum in der Zukunft, andere Klasse oder Art),
  // Zeitraum erweitern bzw. Filter lösen – sonst wäre der gespeicherte Eintrag in der Liste nicht zu sehen
  const neu = v => {
    if (!v?.datum) { kontext.navigieren(location.hash); return; }
    const kl = klasse && klasse.toLowerCase() !== String(v.klasse || '').toLowerCase() ? '' : klasse;
    const ar = art && art !== v.art ? '' : art;
    kontext.navigieren(ziel(v.datum < von ? v.datum : von, v.datum > bis ? v.datum : bis, kl, ar));
  };
  const loeschen = async id => {
    if (!await bestaetigen('Eintrag löschen', 'Den Klassenbucheintrag endgültig löschen?')) return false;
    try { await api(`/api/app/klassenbuch/vermerke/${id}`, { methode: 'DELETE' }); toast('Eintrag gelöscht.'); neu(); return true; } catch (err) { toast(err.message, true); return false; }
  };
  seite.querySelectorAll('[data-weg]').forEach(b => b.onclick = ev => { ev.stopPropagation(); loeschen(b.dataset.weg); });
  seite.querySelectorAll('[data-wahl]').forEach(c => c.onclick = ev => ev.stopPropagation());
  seite.querySelector('[data-alle]')?.addEventListener('change', ev => seite.querySelectorAll('[data-wahl]').forEach(c => { c.checked = ev.target.checked; }));
  seite.querySelectorAll('[data-stift]').forEach(b => b.onclick = ev => { ev.stopPropagation(); eintragDialog(el, daten.eintraege[Number(b.dataset.stift)], daten.arten, neu); });
  seite.querySelector('[data-sammel-loeschen]')?.addEventListener('click', async () => {
    const ids = [...seite.querySelectorAll('[data-wahl]:checked')].map(c => c.dataset.wahl);
    if (!ids.length) { toast('Bitte zuerst Einträge auswählen.', true); return; }
    if (!await bestaetigen('Einträge löschen', `${ids.length === 1 ? 'Den ausgewählten Eintrag' : `${ids.length} Einträge`} endgültig löschen?`)) return;
    try { for (const id of ids) await api(`/api/app/klassenbuch/vermerke/${id}`, { methode: 'DELETE' }); toast('Gelöscht.'); } catch (err) { toast(err.message, true); }
    neu();
  });
  seite.querySelectorAll('tr[data-eintrag]').forEach(tr => {
    const oeffnen = () => detail(daten.eintraege[Number(tr.dataset.eintrag)], { el, arten: daten.arten, loeschen, fertig: neu, stundeText });
    tr.onclick = ev => { if (!ev.target.closest('[data-weg], [data-stift], [data-wahl]')) oeffnen(); };
    tr.onkeydown = ev => { if ((ev.key === 'Enter' || ev.key === ' ') && ev.target === tr) { ev.preventDefault(); oeffnen(); } };
  });
  // neuer Eintrag: gefilterte Klasse, sonst die eigene Klasse (Klassenleitung), sonst die erste
  const eigene = el.klassen.find(k => kontext.ich?.element && (k.leitung || []).some(l => l.toLowerCase() === kontext.ich.element.toLowerCase()))?.kurz;
  document.querySelector('[data-neu]').onclick = () => eintragDialog(el, { klasse: klasse || eigene || el.klassen[0]?.kurz }, daten.arten, neu);
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
      const v = bearbeiten ? await senden(`/api/app/klassenbuch/vermerke/${x.id}`, { ...d, zeit: x.zeit || null }, 'PUT')
        : await senden('/api/app/klassenbuch/vermerke', d);
      toast('Eintrag gespeichert.');
      fertig(v);
    } }],
  });
}
