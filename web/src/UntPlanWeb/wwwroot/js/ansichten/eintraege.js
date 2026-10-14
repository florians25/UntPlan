// Klassenbucheinträge: Vermerke (Lob, Ordnungsmaßnahme, Elterngespräch …) über alle oder eine Klasse, mit Filter und neuem Eintrag.
import { e, icon, api, holen, senden, toast, dialog, bestaetigen, leer, filterFeld, datumKurz, heuteIso, plusTage } from '../ui.js';

const FARBE = { Lob: 'gruen', 'Ordnungsmaßnahme': 'rot', 'Elterngespräch': 'orange', 'Hausaufgabe fehlt': 'gelb', 'Material vergessen': 'gelb' };

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
  seite.innerHTML = `
    <div class="u-filter">
      ${filterFeld('Von', `<input type="date" class="form-control" data-von value="${von}">`)}
      ${filterFeld('Bis', `<input type="date" class="form-control" data-bis value="${bis}">`)}
      ${filterFeld('Klasse', `<select class="form-select" data-klasse><option value="">alle</option>${el.klassen.map(k => `<option ${k.kurz === klasse ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select>`)}
      ${filterFeld('Art', `<select class="form-select" data-art><option value="">alle</option>${daten.arten.map(a => `<option ${a === art ? 'selected' : ''}>${e(a)}</option>`).join('')}</select>`)}
    </div>
    <div class="card">${daten.eintraege.length ? `<div class="table-responsive"><table class="table table-hover table-card align-middle"><thead><tr><th>Datum</th><th>Klasse</th><th>Schüler*in</th><th>Art</th><th>Text</th><th>Lehrkraft</th><th></th></tr></thead><tbody>
      ${daten.eintraege.map(x => `<tr><td class="text-nowrap">${e(datumKurz(x.datum))}${x.datum.slice(0, 4)}</td><td class="fw-semibold">${e(x.klasse)}</td><td>${e(x.schueler || 'ganze Klasse')}</td>
        <td><span class="u-chip ${FARBE[x.art] ?? ''}">${e(x.art)}</span></td><td>${e(x.text)}</td><td class="text-body-secondary">${e(x.lehrer)}</td>
        <td class="text-end"><button class="btn btn-sm btn-link text-danger p-0" data-weg="${x.id}" title="Löschen" aria-label="Löschen">${icon('loeschen')}</button></td></tr>`).join('')}
      </tbody></table></div>` : leer('Keine Klassenbucheinträge im Zeitraum.', 'eintraege')}</div>`;
  const f = n => seite.querySelector(`[data-${n}]`)?.value ?? '';
  seite.querySelectorAll('.u-filter input, .u-filter select').forEach(x => x.onchange = () => kontext.navigieren(ziel(f('von') || von, f('bis') || bis, f('klasse'), f('art'))));
  const neu = () => kontext.navigieren(location.hash);
  seite.querySelectorAll('[data-weg]').forEach(b => b.onclick = async () => {
    if (!await bestaetigen('Eintrag löschen', 'Den Klassenbucheintrag endgültig löschen?')) return;
    try { await api(`/api/app/klassenbuch/vermerke/${b.dataset.weg}`, { methode: 'DELETE' }); toast('Eintrag gelöscht.'); neu(); } catch (err) { toast(err.message, true); }
  });
  document.querySelector('[data-neu]').onclick = () => neuerEintrag(el, klasse || el.klassen[0]?.kurz, daten.arten, neu);
}

async function neuerEintrag(el, klasse, arten, fertig) {
  const laden = k => holen(`/api/app/klassenbuch/schueler?klasse=${encodeURIComponent(k)}`).catch(() => []);
  const schueler = await laden(klasse);
  const optionen = l => `<option value="">ganze Klasse</option>${l.map(x => `<option value="${x.id}">${e(x.name)}, ${e(x.vorname)}</option>`).join('')}`;
  dialog({
    titel: 'Neuer Klassenbucheintrag',
    inhalt: `<div class="row g-3">
      <div class="col-sm-6"><label class="form-label">Klasse</label><select class="form-select" name="klasse">${el.klassen.map(k => `<option ${k.kurz === klasse ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select></div>
      <div class="col-sm-6"><label class="form-label">Datum</label><input type="date" class="form-control" name="datum" value="${heuteIso()}"></div>
      <div class="col-sm-6"><label class="form-label">Art</label><select class="form-select" name="art">${arten.map(a => `<option>${e(a)}</option>`).join('')}</select></div>
      <div class="col-sm-6"><label class="form-label">Betrifft</label><select class="form-select" name="schueler">${optionen(schueler)}</select></div>
      <div class="col-12"><label class="form-label">Text</label><textarea class="form-control" name="text" rows="3" required></textarea></div></div>`,
    beimOeffnen: box => {
      box.querySelector('[name=klasse]').onchange = async ev => { box.querySelector('[name=schueler]').innerHTML = optionen(await laden(ev.target.value)); };
    },
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
      const f = n => box.querySelector(`[name=${n}]`).value;
      await senden('/api/app/klassenbuch/vermerke', { datum: f('datum'), klasse: f('klasse'), schuelerId: f('schueler') ? Number(f('schueler')) : null, art: f('art'), text: f('text') });
      toast('Eintrag gespeichert.');
      fertig();
    } }],
  });
}
