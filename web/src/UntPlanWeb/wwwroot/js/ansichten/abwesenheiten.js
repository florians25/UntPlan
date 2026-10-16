// Klassenbuch → Abwesenheiten wie im Original: Liste der Schülerabwesenheiten mit Filter (Klasse, Schüler*in, Zeitraum, Grund, Status),
// Neu/Bearbeiten/Löschen, Zusammenfassen und Entschuldigen durch die Klassenleitung, CSV; Reiter „Meine Abwesenheiten“ (eigene Meldungen).
import { e, icon, holen, senden, api, toast, dialog, bestaetigen, leer, filterFeld, datumKachel, stundenText, heuteIso, plusTage, meldungText, statusMarker, herunterladen } from '../ui.js';

const STATUS = { offen: '<span class="u-chip gelb">offen</span>', entschuldigt: '<span class="u-chip gruen">entschuldigt</span>', unentschuldigt: '<span class="u-chip rot">nicht entschuldigt</span>' };
const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';
/** Avatar mit Initialen (Nachname, Vorname) wie in der neuen Oberfläche; hervorgehoben = gefüllt. */
export const avatar = (name, gefuellt = true) => {
  const teile = String(name || '').replace(',', ' ').split(/\s+/).filter(Boolean);
  const ini = ((teile[0]?.[0] ?? '') + (teile[1]?.[0] ?? '')).toUpperCase();
  return `<span class="nl-avatar ${gefuellt ? 'voll' : ''}" aria-hidden="true">${e(ini)}</span>`;
};
const tagKurz = iso => new Date(iso + 'T12:00').toLocaleDateString('de-DE', { weekday: 'short', day: '2-digit', month: '2-digit' });

/** Zeilen einer zusammengefassten Abwesenheit (gleiche Gruppe) zu einer Zeile über mehrere Tage verbinden. */
function zusammenfassen(liste) {
  const res = [];
  const gruppen = new Map();
  for (const z of liste) {
    if (!z.gruppe) { res.push({ ...z, bis: z.datum }); continue; }
    const k = `${z.schuelerId}|${z.gruppe}`;
    const g = gruppen.get(k);
    if (!g) { const n = { ...z, bis: z.datum, ids: [...z.ids], faecher: [...z.faecher], lehrer: [...z.lehrer], stundenSumme: z.stundeBis - z.stundeVon + 1 }; gruppen.set(k, n); res.push(n); continue; }
    if (z.datum < g.datum) { g.datum = z.datum; g.stundeVon = z.stundeVon; g.beginn = z.beginn; }
    if (z.datum > g.bis) { g.bis = z.datum; g.stundeBis = z.stundeBis; g.ende = z.ende; }
    g.ids.push(...z.ids);
    g.stundenSumme += z.stundeBis - z.stundeVon + 1;
    z.faecher.forEach(f => { if (!g.faecher.includes(f)) g.faecher.push(f); });
    z.lehrer.forEach(f => { if (!g.lehrer.includes(f)) g.lehrer.push(f); });
  }
  return res;
}

const ARTEN = { fehlt: 'abwesend', verspaetet: 'verspätet', vorzeitig: 'vorzeitig gegangen' };
const artChip = z => z.art === 'verspaetet' ? `<span class="u-chip gelb"><i class="bi bi-clock me-1"></i>verspätet ${z.minuten} min</span>`
  : z.art === 'vorzeitig' ? `<span class="u-chip lila"><i class="bi bi-box-arrow-right me-1"></i>vorzeitig ${z.minuten} min</span>`
  : /^(verspätet|vorzeitig gegangen)/.test(z.bemerkung || '') ? `abwesend <span class="small text-body-secondary">(${e(z.bemerkung.match(/^(verspätet|vorzeitig gegangen)/)[1])})</span>` : 'abwesend';

export async function zeigen(seite, kontext, [reiterParam, vonParam, bisParam, klasseParam, statusParam, grundParam, artParam]) {
  const ich = kontext.ich;
  const el = await kontext.elemente();
  const reiter = reiterParam === 'meine' && ich.element ? 'meine' : 'schueler';
  const iso = /^\d{4}-\d{2}-\d{2}$/;
  const von = iso.test(vonParam ?? '') ? vonParam : plusTage(heuteIso(), -14);
  const bis = iso.test(bisParam ?? '') ? bisParam : heuteIso();
  const klasse = klasseParam && klasseParam !== '-' ? klasseParam : '';
  const status = ['offen', 'entschuldigt', 'unentschuldigt'].includes(statusParam) ? statusParam : '';
  const grund = grundParam && grundParam !== '-' ? grundParam : '';
  const art = ARTEN[artParam] ? artParam : '';
  const ziel = (r, v = von, b = bis, k = klasse, s = status, g = grund, a = art) => `#/abwesenheiten/${r}/${v}/${b}/${encodeURIComponent(k || '-')}/${s || '-'}/${encodeURIComponent(g || '-')}${a ? `/${a}` : ''}`;
  seite.innerHTML = `
    ${ich.element ? `<nav class="u-reiter mb-3"><a href="${ziel('schueler')}" class="${reiter === 'schueler' ? 'aktiv' : ''}">Schüler*innen</a><a href="${ziel('meine')}" class="${reiter === 'meine' ? 'aktiv' : ''}">Meine Abwesenheiten</a></nav>` : ''}
    <div data-inhalt></div>`;
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
  const [roh, gruende] = await Promise.all([holen(`/api/app/abwesenheiten?${q}`), holen('/api/app/abwesenheitsgruende?art=abwesenheit').catch(() => [])]);
  const grundName = Object.fromEntries(gruende.map(g => [g.kurz, g.name]));
  const liste = zusammenfassen(roh.filter(z => (!grund || z.grund === grund) && (!art || z.art === art)));
  const alleZeilen = zusammenfassen(roh);
  const anzahl = { offen: alleZeilen.filter(z => z.status === 'offen' && z.art === 'fehlt').length, unent: alleZeilen.filter(z => z.status === 'unentschuldigt' && z.art === 'fehlt').length,
    spaet: roh.filter(z => z.art === 'verspaetet').length, frueh: roh.filter(z => z.art === 'vorzeitig').length,
    minuten: roh.filter(z => z.art !== 'fehlt').reduce((n, z) => n + z.minuten, 0) };
  const verwalten = roh.some(z => z.verwalten) || ich.admin;
  kontext.titel('Abwesenheiten', '', `${liste.length}`);
  const pille = (name, inhaltHtml, aktiv) => `<label class="nl-pille ${aktiv ? 'aktiv' : ''}"><span class="visually-hidden">${name}</span>${inhaltHtml}</label>`;
  inhalt.innerHTML = `
    <div class="nl-leiste">
      <label class="nl-suche"><i class="bi bi-search" aria-hidden="true"></i><input type="search" data-suche placeholder="Suchen" aria-label="Schüler*in suchen"></label>
      ${pille('Klasse', `<select data-klasse><option value="">Klasse</option>${el.klassen.map(k => `<option value="${e(k.kurz)}" ${k.kurz === klasse ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select>`, klasse)}
      ${pille('Von', `<input type="date" data-von value="${von}">`, false)}
      ${pille('Bis', `<input type="date" data-bis value="${bis}">`, false)}
      ${pille('Abwesenheitsgrund', `<select data-grund><option value="">Abwesenheitsgrund</option>${gruende.map(g => `<option value="${e(g.kurz)}" ${g.kurz === grund ? 'selected' : ''}>${e(g.name)}</option>`).join('')}</select>`, grund)}
      ${pille('Art', `<select data-art><option value="">Art</option>${Object.entries(ARTEN).map(([k, t]) => `<option value="${k}" ${k === art ? 'selected' : ''}>${t}</option>`).join('')}</select>`, art)}
      ${pille('Status', `<select data-status><option value="">Status</option>${['offen', 'entschuldigt', 'unentschuldigt'].map(x => `<option value="${x}" ${x === status ? 'selected' : ''}>${x === 'unentschuldigt' ? 'nicht entschuldigt' : x}</option>`).join('')}</select>`, status)}
      <span class="ms-auto d-flex gap-2">
        <button type="button" class="nl-knopf" data-csv title="Als CSV herunterladen">${icon('csv')}Bericht</button>
        ${verwalten ? `<button type="button" class="nl-hinzu" data-neu><i class="bi bi-plus-lg"></i>Hinzufügen</button>` : ''}
      </span>
    </div>
    <div class="nl-zahlen">
      <button type="button" class="nl-zahl ${status === 'offen' ? 'aktiv' : ''}" data-kz="offen"><b>${anzahl.offen}</b> offen</button>
      <button type="button" class="nl-zahl rot ${status === 'unentschuldigt' ? 'aktiv' : ''}" data-kz="unentschuldigt"><b>${anzahl.unent}</b> nicht entschuldigt</button>
      <button type="button" class="nl-zahl gelb ${art === 'verspaetet' ? 'aktiv' : ''}" data-kz="verspaetet"><b>${anzahl.spaet}</b> Verspätungen</button>
      <button type="button" class="nl-zahl lila ${art === 'vorzeitig' ? 'aktiv' : ''}" data-kz="vorzeitig"><b>${anzahl.frueh}</b> vorzeitig gegangen</button>
      <span class="nl-zahl still"><b>${anzahl.minuten}</b> Minuten</span>
    </div>
    <div class="nl-tabelle">${liste.length ? `<table><thead><tr>${verwalten ? '<th class="nl-wahl"><input type="checkbox" class="nl-rund" data-alle aria-label="Alle auswählen"></th>' : ''}
        <th colspan="2">Name</th><th>Zeitraum</th><th>Art</th><th>Grund</th><th>Status</th><th>Text</th>${verwalten ? '<th></th>' : ''}</tr></thead><tbody>
      ${liste.map((z, i) => `<tr data-name="${e((z.schueler + ' ' + z.klasse).toLowerCase())}">${verwalten ? `<td class="nl-wahl">${z.verwalten || ich.admin ? `<input type="checkbox" class="nl-rund" data-i="${i}" aria-label="auswählen">` : ''}</td>` : ''}
        <td class="nl-avatar-zelle">${avatar(z.schueler, z.status === 'offen')}</td>
        <td><div class="nl-name">${e(z.schueler)}</div><div class="nl-unter">${e(z.klasse)}${z.lehrer.length ? ` · erfasst von ${e(z.lehrer.join(', '))}` : ''}</div></td>
        <td class="text-nowrap"><div>${tagKurz(z.datum)}${z.bis !== z.datum ? ` – ${tagKurz(z.bis)}` : ''}</div>
          <div class="nl-unter">${z.bis !== z.datum ? `${z.stundenSumme} Std.` : stundenText(z.stundeVon, z.stundeBis)}${z.beginn ? ` · ${e(z.beginn)}–${e(z.ende)}` : ''}</div></td>
        <td>${artChip(z)}</td>
        <td>${z.grund ? `<span class="nl-tag">${e(grundName[z.grund] ?? z.grund)}</span>` : ''}</td><td>${STATUS[z.status] ?? e(z.status)}</td><td class="nl-text">${e(z.bemerkung)}</td>
        ${verwalten ? `<td class="text-end text-nowrap nl-aktionen">${z.verwalten || ich.admin ? `<button class="nl-icon" data-bearbeiten="${i}" title="Bearbeiten" aria-label="Bearbeiten">${icon('stift')}</button>
          <button class="nl-icon rot" data-loeschen="${i}" title="Löschen" aria-label="Löschen">${icon('loeschen')}</button>` : ''}</td>` : ''}</tr>`).join('')}
      </tbody></table>
      ${verwalten ? `<div class="nl-fuss"><span class="nl-unter me-2">${liste.length} Elemente</span>
        <button class="nl-knopf gruen" data-setzen="entschuldigt">Entschuldigen</button><button class="nl-knopf rot" data-setzen="unentschuldigt">Nicht entschuldigt</button>
        <button class="nl-knopf" data-setzen="offen">Offen</button><button class="nl-knopf" data-zusammen>Zusammenfassen</button>
        <a class="nl-knopf ms-auto" href="#/berichte/abwesenheiten/${encodeURIComponent(klasse || '-')}/${von}/${bis}">Berichte</a></div>` : ''}`
      : `<div class="p-3">${leer('Keine Abwesenheiten im Zeitraum.', 'person-check')}</div>`}</div>`;
  inhalt.querySelector('[data-suche]')?.addEventListener('input', ev => {
    const t = ev.target.value.trim().toLowerCase();
    inhalt.querySelectorAll('.nl-tabelle tbody tr').forEach(tr => { tr.hidden = t.length > 0 && !tr.dataset.name.includes(t); });
  });
  const f = n => inhalt.querySelector(`[data-${n}]`)?.value ?? '';
  inhalt.querySelectorAll('.nl-pille input, .nl-pille select').forEach(x => x.onchange = () => kontext.navigieren(ziel('schueler', f('von') || von, f('bis') || bis, f('klasse'), f('status'), f('grund'), f('art'))));
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
    if (b.dataset.setzen !== 'entschuldigt') {
      try { await senden('/api/app/klassenbuch/fehlzeiten/status', { ids, status: b.dataset.setzen }); toast('Status geändert.'); neu(); } catch (err) { toast(err.message, true); }
      return;
    }
    dialog({ titel: 'Entschuldigen', inhalt: `<p class="small">${auswahl.length} Abwesenheit(en) entschuldigen.</p><label class="form-label">Text</label><input class="form-control" name="text" list="abw-texte" placeholder="z. B. Attest liegt vor">${textListe}`,
      knoepfe: [{ text: 'Abbrechen' }, { text: 'Entschuldigen', art: 'haupt', aktion: async box => {
        const t = box.querySelector('[name=text]').value.trim();
        await senden('/api/app/abwesenheiten', { ids, status: 'entschuldigt', bemerkung: t || null }, 'PUT');
        toast('Entschuldigt.'); neu();
      } }] });
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
  inhalt.querySelectorAll('[data-bearbeiten]').forEach(b => b.onclick = () => {
    const z = liste[Number(b.dataset.bearbeiten)];
    dialog({ titel: `Abwesenheit ${z.schueler}`, inhalt: `<p class="small text-body-secondary">${datumDe(z.datum)}${z.bis !== z.datum ? ' – ' + datumDe(z.bis) : `, ${stundenText(z.stundeVon, z.stundeBis)} Std.`} · ${e(z.faecher.join(', '))}</p>
        <div class="row g-3"><div class="col-sm-6"><label class="form-label">Abwesenheitsgrund</label><select class="form-select" name="grund"><option value="">–</option>${gruende.map(g => `<option value="${e(g.kurz)}" ${g.kurz === z.grund ? 'selected' : ''}>${e(g.name)}</option>`).join('')}</select></div>
        <div class="col-sm-6"><label class="form-label">Status</label><select class="form-select" name="status">${['offen', 'entschuldigt', 'unentschuldigt'].map(s => `<option value="${s}" ${s === z.status ? 'selected' : ''}>${s === 'unentschuldigt' ? 'nicht entschuldigt' : s}</option>`).join('')}</select></div>
        <div class="col-12"><label class="form-label">Text</label><input class="form-control" name="bemerkung" value="${e(z.bemerkung)}" list="abw-texte">${textListe}</div></div>`,
      knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
        const v = n => box.querySelector(`[name=${n}]`).value;
        await senden('/api/app/abwesenheiten', { ids: z.ids, grund: v('grund'), status: v('status'), bemerkung: v('bemerkung') }, 'PUT');
        toast('Gespeichert.'); neu();
      } }] });
  });
  document.querySelector('[data-neu]')?.addEventListener('click', async () => {
    const kl = klasse || el.klassen.find(k => ich.element && (k.leitung ?? []).includes(ich.element))?.kurz || el.klassen[0]?.kurz;
    const schueler = kl ? await holen(`/api/app/klassenbuch/schueler?klasse=${encodeURIComponent(kl)}`) : [];
    const stunden = el.stunden ?? [];
    dialog({ titel: 'Neue Abwesenheit', breit: true,
      inhalt: `<div class="row g-3">
        <div class="col-sm-4"><label class="form-label">Klasse</label><select class="form-select" name="klasse">${el.klassen.map(k => `<option ${k.kurz === kl ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select></div>
        <div class="col-sm-8"><label class="form-label">Schüler*in</label><select class="form-select" name="schuelerId">${schueler.map(s => `<option value="${s.id}">${e(s.name)} ${e(s.vorname)}</option>`).join('')}</select></div>
        <div class="col-sm-3"><label class="form-label">Von</label><input type="date" class="form-control" name="von" value="${heuteIso()}"></div>
        <div class="col-sm-3"><label class="form-label">ab Stunde</label><select class="form-select" name="stundeVon">${stunden.map(z => `<option value="${z.nr}">${z.nr}. (${e(z.beginn)})</option>`).join('')}</select></div>
        <div class="col-sm-3"><label class="form-label">Bis</label><input type="date" class="form-control" name="bis" value="${heuteIso()}"></div>
        <div class="col-sm-3"><label class="form-label">bis Stunde</label><select class="form-select" name="stundeBis">${stunden.map((z, i) => `<option value="${z.nr}" ${i === stunden.length - 1 ? 'selected' : ''}>${z.nr}. (${e(z.ende)})</option>`).join('')}</select></div>
        <div class="col-sm-6"><label class="form-label">Abwesenheitsgrund</label><select class="form-select" name="grund"><option value="">–</option>${gruende.map(g => `<option value="${e(g.kurz)}">${e(g.name)}</option>`).join('')}</select></div>
        <div class="col-sm-6"><label class="form-label">Status</label><select class="form-select" name="status"><option value="offen">offen</option><option value="entschuldigt">entschuldigt</option><option value="unentschuldigt">nicht entschuldigt</option></select></div>
        <div class="col-12"><label class="form-label">Text</label><input class="form-control" name="bemerkung" list="abw-texte">${textListe}</div></div>`,
      beimOeffnen: box => {
        box.querySelector('[name=klasse]').onchange = async ev => {
          const s = await holen(`/api/app/klassenbuch/schueler?klasse=${encodeURIComponent(ev.target.value)}`);
          box.querySelector('[name=schuelerId]').innerHTML = s.map(x => `<option value="${x.id}">${e(x.name)} ${e(x.vorname)}</option>`).join('');
        };
      },
      knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
        const v = n => box.querySelector(`[name=${n}]`).value;
        const r = await senden('/api/app/abwesenheiten', { schuelerId: Number(v('schuelerId')), von: v('von'), bis: v('bis'), stundeVon: Number(v('stundeVon')), stundeBis: Number(v('stundeBis')), grund: v('grund'), status: v('status'), bemerkung: v('bemerkung') });
        toast(`${r.stunden} Stunden eingetragen.`); neu();
      } }] });
  });
  document.querySelector('[data-csv]')?.addEventListener('click', () => herunterladen(`abwesenheiten-${von}-${bis}.csv`,
    ['Schüler*in;Klasse;Von;Bis;Stunden;Art;Minuten;Grund;Fach;Status;Text;erfasst von', ...liste.map(z => [z.schueler, z.klasse, z.datum, z.bis, z.bis !== z.datum ? z.stundenSumme : stundenText(z.stundeVon, z.stundeBis), z.art, z.minuten, grundName[z.grund] ?? z.grund, z.faecher.join(', '), z.status, z.bemerkung, z.lehrer.join(', ')]
      .map(x => `"${String(x ?? '').replace(/"/g, '""')}"`).join(';'))].join('\r\n')));
}
