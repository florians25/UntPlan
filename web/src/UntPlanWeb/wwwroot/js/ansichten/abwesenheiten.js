// Klassenbuch → Abwesenheiten wie im Original: Liste der Schülerabwesenheiten mit Filter (Klasse, Schüler*in, Zeitraum, Grund, Status),
// Neu/Bearbeiten/Löschen, Zusammenfassen und Entschuldigen durch die Klassenleitung, CSV; Reiter „Meine Abwesenheiten“ (eigene Meldungen).
import { e, icon, holen, senden, api, toast, dialog, bestaetigen, leer, filterFeld, datumKachel, stundenText, heuteIso, plusTage, meldungText, statusMarker, herunterladen } from '../ui.js';

const STATUS = { offen: '<span class="u-chip gelb">offen</span>', entschuldigt: '<span class="u-chip gruen">entschuldigt</span>', unentschuldigt: '<span class="u-chip rot">nicht entschuldigt</span>' };
const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';
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
  kontext.titel('Abwesenheiten', `${verwalten ? `<button class="btn btn-primary" data-neu>${icon('plus')}Neu</button>` : ''}<button class="btn btn-outline-secondary" data-csv>${icon('csv')}Bericht</button>`, `${liste.length}`);
  inhalt.innerHTML = `
    <div class="u-filter">
      ${filterFeld('Klasse', `<select class="form-select" data-klasse><option value="">alle</option>${el.klassen.map(k => `<option ${k.kurz === klasse ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select>`)}
      ${filterFeld('Von', `<input type="date" class="form-control" data-von value="${von}">`)}
      ${filterFeld('Bis', `<input type="date" class="form-control" data-bis value="${bis}">`)}
      ${filterFeld('Abwesenheitsgrund', `<select class="form-select" data-grund><option value="">- Alle -</option>${gruende.map(g => `<option value="${e(g.kurz)}" ${g.kurz === grund ? 'selected' : ''}>${e(g.name)}</option>`).join('')}</select>`)}
      ${filterFeld('Art', `<select class="form-select" data-art><option value="">- Alle -</option>${Object.entries(ARTEN).map(([k, t]) => `<option value="${k}" ${k === art ? 'selected' : ''}>${t}</option>`).join('')}</select>`)}
      ${filterFeld('Status', `<select class="form-select" data-status><option value="">- Alle -</option>${['offen', 'entschuldigt', 'unentschuldigt'].map(s => `<option value="${s}" ${s === status ? 'selected' : ''}>${s === 'unentschuldigt' ? 'nicht entschuldigt' : s}</option>`).join('')}</select>`)}
    </div>
    <div class="abw-kennzahlen">
      <button type="button" class="abw-kz ${status === 'offen' ? 'aktiv' : ''}" data-kz="offen"><b>${anzahl.offen}</b><span>offen</span></button>
      <button type="button" class="abw-kz rot ${status === 'unentschuldigt' ? 'aktiv' : ''}" data-kz="unentschuldigt"><b>${anzahl.unent}</b><span>nicht entschuldigt</span></button>
      <button type="button" class="abw-kz gelb ${art === 'verspaetet' ? 'aktiv' : ''}" data-kz="verspaetet"><b>${anzahl.spaet}</b><span>Verspätungen</span></button>
      <button type="button" class="abw-kz lila ${art === 'vorzeitig' ? 'aktiv' : ''}" data-kz="vorzeitig"><b>${anzahl.frueh}</b><span>vorzeitig gegangen</span></button>
      <div class="abw-kz still"><b>${anzahl.minuten}</b><span>Minuten gesamt</span></div>
    </div>
    <div class="card">${liste.length ? `<div class="table-responsive"><table class="table table-hover table-card align-middle"><thead><tr>${verwalten ? '<th><input type="checkbox" class="form-check-input" data-alle aria-label="Alle auswählen"></th>' : ''}
        <th>Schüler*in</th><th>Klasse</th><th>Von</th><th>Bis</th><th>Stunden</th><th>Art</th><th>Grund</th><th>Status</th><th>Text</th><th>erfasst von</th>${verwalten ? '<th></th>' : ''}</tr></thead><tbody>
      ${liste.map((z, i) => `<tr>${verwalten ? `<td>${z.verwalten || ich.admin ? `<input type="checkbox" class="form-check-input" data-i="${i}" aria-label="auswählen">` : ''}</td>` : ''}
        <td class="fw-semibold">${e(z.schueler)}</td><td>${e(z.klasse)}</td><td class="text-nowrap">${tagKurz(z.datum)}</td><td class="text-nowrap">${z.bis !== z.datum ? tagKurz(z.bis) : ''}</td>
        <td class="text-nowrap">${z.bis !== z.datum ? `${z.stundenSumme} Std.` : stundenText(z.stundeVon, z.stundeBis)}${z.beginn ? `<div class="small text-body-secondary">${e(z.beginn)}–${e(z.ende)}</div>` : ''}</td>
        <td>${artChip(z)}</td>
        <td>${e(grundName[z.grund] ?? z.grund)}</td><td>${STATUS[z.status] ?? e(z.status)}</td><td class="small">${e(z.bemerkung)}</td><td class="small text-body-secondary">${e(z.lehrer.join(', '))}</td>
        ${verwalten ? `<td class="text-end text-nowrap">${z.verwalten || ich.admin ? `<button class="btn btn-sm btn-outline-secondary" data-bearbeiten="${i}" title="Bearbeiten">${icon('stift')}</button>
          <button class="btn btn-sm btn-outline-danger" data-loeschen="${i}" title="Löschen">${icon('loeschen')}</button>` : ''}</td>` : ''}</tr>`).join('')}
      </tbody></table></div>
      ${verwalten ? `<div class="card-footer d-flex flex-wrap gap-2 align-items-center"><span class="small text-body-secondary me-2">${liste.length} Elemente gefunden.</span>
        <button class="btn btn-sm btn-outline-success" data-setzen="entschuldigt">Entschuldigen</button><button class="btn btn-sm btn-outline-danger" data-setzen="unentschuldigt">Nicht entschuldigt</button>
        <button class="btn btn-sm btn-outline-secondary" data-setzen="offen">Offen</button><button class="btn btn-sm btn-outline-secondary" data-zusammen>Zusammenfassen</button>
        <a class="btn btn-sm btn-outline-secondary ms-auto" href="#/berichte/abwesenheiten/${encodeURIComponent(klasse || '-')}/${von}/${bis}">Berichte</a></div>` : ''}`
      : `<div class="card-body py-0">${leer('Keine Abwesenheiten im Zeitraum.', 'person-check')}</div>`}</div>`;
  const f = n => inhalt.querySelector(`[data-${n}]`)?.value ?? '';
  inhalt.querySelectorAll('.u-filter input, .u-filter select').forEach(x => x.onchange = () => kontext.navigieren(ziel('schueler', f('von') || von, f('bis') || bis, f('klasse'), f('status'), f('grund'), f('art'))));
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
