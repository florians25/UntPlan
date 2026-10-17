// Neuigkeiten: Infos der Schule und Neuigkeiten zu UntPlanWeb als ruhige, einheitliche Liste (wie im Infocenter), Filter nach Art.
// #/neuigkeiten/verwalten: Infos anlegen, bearbeiten, löschen (Administration, Schulleitung).
import { e, holen, senden, api, dialog, bestaetigen, toast, fehler, suchfeldMitFilter, suchfeldMitFilterBinden, zeilenFiltern } from '../ui.js';
import { laden, gelesen, karteHtml, mehrBinden, datumText, beobachten, leerHtml } from '../infocenter.js';

const ZIELGRUPPEN = { alle: 'Alle', kollegium: 'Lehrkräfte', schueler: 'Schüler*innen', eltern: 'Eltern' };

export async function zeigen(seite, kontext, [teil, filterParam]) {
  if (teil === 'verwalten') return verwalten(seite, kontext);
  const d = await laden();
  const filter = ['infos', 'updates'].includes(teil) ? teil : ['infos', 'updates'].includes(filterParam) ? filterParam : 'alle';
  const liste = d.eintraege.filter(x => filter === 'alle' || (filter === 'infos' ? x.art === 'info' : x.art === 'neuigkeit'));
  const ungelesen = new Set(d.eintraege.filter(x => !x.gelesen).map(x => x.schluessel));
  const zahl = art => d.eintraege.filter(x => !x.gelesen && (!art || x.art === art)).length;
  const anzahl = art => d.eintraege.filter(x => !art || x.art === art).length;
  const optionText = (text, art) => `${text} (${anzahl(art)}${zahl(art) ? `, ${zahl(art)} neu` : ''})`;
  kontext.titel('Neuigkeiten', `${ungelesen.size ? '<button type="button" class="btn btn-outline-secondary" data-alle-gelesen><i class="bi bi-check2-all"></i>Alle als gelesen markieren</button>' : ''}
    ${d.darfVerwalten ? '<a class="btn btn-primary" href="#/neuigkeiten/verwalten"><i class="bi bi-pencil-square"></i>Infos verwalten</a>' : ''}`, ungelesen.size ? `${ungelesen.size} ungelesen` : '');
  seite.innerHTML = `<div class="nw-seite">
    <p class="nw-unterzeile">Infos der Schule und Neuerungen in UntPlanWeb – neue Einträge sind mit einem Punkt markiert.</p>
    <div class="u-sf-leiste">${suchfeldMitFilter({ platzhalter: 'Neuigkeiten durchsuchen', label: 'Neuigkeiten durchsuchen', filter: [
      { name: 'art', label: 'Art', alle: optionText('Alle'), standard: '', wert: filter === 'alle' ? '' : filter, optionen: [['infos', optionText('Infos der Schule', 'info')], ['updates', optionText('UntPlanWeb-Updates', 'neuigkeit')]] },
    ] })}</div>
    ${liste.length ? `<div class="nw-liste-karte" data-nw-liste>${liste.map(x => karteHtml(x)).join('')}</div>`
      : `<div class="nw-liste-karte">${leerHtml(filter === 'infos' ? 'Keine Infos der Schule' : 'Keine Neuigkeiten', filter === 'infos' ? 'Die Schule hat aktuell keine Infos veröffentlicht.' : 'Sobald es etwas Neues gibt, erscheint es hier.', filter === 'infos' ? 'megaphone' : 'bell-slash')}</div>`}
  </div>`;
  mehrBinden(seite.querySelector('[data-nw-liste]'));
  suchfeldMitFilterBinden(seite, {
    beiSuche: t => zeilenFiltern(seite.querySelectorAll('[data-nw-liste] .ic-karte'), t),
    beiFilter: (_, wert) => kontext.navigieren(`#/neuigkeiten${wert ? '/' + wert : ''}`),
  });
  document.querySelector('[data-alle-gelesen]')?.addEventListener('click', async () => { await gelesen(); kontext.navigieren(location.hash); });
  // beim Ansehen gelten die gezeigten Einträge als gelesen; die Markierung bleibt bis zum nächsten Aufruf sichtbar
  const gezeigt = liste.filter(x => !x.gelesen).map(x => x.schluessel);
  if (gezeigt.length) await gelesen(gezeigt);
  seite.querySelectorAll('.ic-karte').forEach(k => k.classList.toggle('neu', ungelesen.has(k.dataset.schluessel)));
}

// ------------------------------------------------------------------ Verwaltung

const zeitraum = i => !i.von && !i.bis ? 'unbegrenzt' : `${i.von ? datumText(i.von) : '…'} – ${i.bis ? datumText(i.bis) : '…'}`;

async function verwalten(seite, kontext) {
  kontext.titel('');
  let infos;
  try { infos = await holen('/api/app/infocenter/infos'); }
  catch (err) { seite.innerHTML = `<div class="fm-karte"><div class="fm-leer gross"><i class="bi bi-lock"></i><span>${e(err.message)}</span></div></div>`; return; }
  const heute = new Date().toISOString().slice(0, 10);
  const aktiv = i => (!i.von || i.von <= heute) && (!i.bis || i.bis >= heute);
  kontext.titel('Infos verwalten', '<a class="btn btn-outline-secondary" href="#/neuigkeiten"><i class="bi bi-arrow-left"></i>Neuigkeiten</a><button type="button" class="btn btn-primary" data-neu><i class="bi bi-plus-lg"></i>Info erstellen</button>');
  seite.innerHTML = `<p class="nw-hinweis">Infos erscheinen im Infocenter (Glocke unten rechts) bei der gewählten Zielgruppe – im angegebenen Zeitraum. Wichtige Infos stehen oben.</p>
    ${infos.length ? `<div class="nl-tabelle nw-tabelle"><table><thead><tr><th>Titel</th><th>Zielgruppe</th><th>Gültig</th><th>Status</th><th class="text-end">Aktionen</th></tr></thead><tbody>
      ${infos.map(i => `<tr><td><b>${e(i.titel)}</b>${i.wichtig ? ' <span class="ic-kat wichtig">Wichtig</span>' : ''}<div class="nw-grau">${e(i.text).slice(0, 140)}</div></td>
        <td>${e(ZIELGRUPPEN[i.zielgruppe] ?? i.zielgruppe)}</td><td class="text-nowrap">${e(zeitraum(i))}</td>
        <td>${aktiv(i) ? '<span class="nl-tag nw-sichtbar">sichtbar</span>' : '<span class="nl-tag">nicht sichtbar</span>'}</td>
        <td class="text-end text-nowrap"><button type="button" class="nw-symbol" data-bearbeiten="${i.id}" title="Bearbeiten" aria-label="Bearbeiten"><i class="bi bi-pencil"></i></button>
          <button type="button" class="nw-symbol" data-loeschen="${i.id}" title="Löschen" aria-label="Löschen"><i class="bi bi-trash"></i></button></td></tr>`).join('')}
      </tbody></table></div>`
      : '<div class="fm-karte"><div class="fm-leer gross"><i class="bi bi-megaphone"></i><span>Noch keine Infos angelegt</span></div></div>'}`;
  const neu = () => verwalten(seite, kontext);
  document.querySelector('#aktionen [data-neu]').onclick = () => formular(null, neu);
  seite.querySelectorAll('[data-bearbeiten]').forEach(b => b.onclick = () => formular(infos.find(i => i.id === Number(b.dataset.bearbeiten)), neu));
  seite.querySelectorAll('[data-loeschen]').forEach(b => b.onclick = async () => {
    const i = infos.find(x => x.id === Number(b.dataset.loeschen));
    if (!await bestaetigen('Info löschen', `„${i.titel}“ wirklich löschen?`)) return;
    try { await api(`/api/app/infocenter/infos/${i.id}`, { methode: 'DELETE' }); toast('Info gelöscht.'); await laden(); neu(); }
    catch (err) { fehler(err); }
  });
}

function formular(info, fertig) {
  const i = info ?? { titel: '', text: '', zielgruppe: 'alle', von: '', bis: '', wichtig: false };
  dialog({
    titel: info ? 'Info bearbeiten' : 'Info erstellen',
    inhalt: `<div class="nw-formular">
      <label class="form-label" for="nw-titel">Titel</label><input class="form-control" id="nw-titel" name="titel" maxlength="200" value="${e(i.titel)}" required>
      <label class="form-label" for="nw-text">Text</label><textarea class="form-control" id="nw-text" name="text" rows="5" maxlength="4000">${e(i.text)}</textarea>
      <label class="form-label" for="nw-ziel">Zielgruppe</label><select class="form-select" id="nw-ziel" name="zielgruppe">${Object.entries(ZIELGRUPPEN).map(([k, t]) => `<option value="${k}" ${k === i.zielgruppe ? 'selected' : ''}>${t}</option>`).join('')}</select>
      <div class="nw-zeitraum"><div><label class="form-label" for="nw-von">Gültig von</label><input type="date" class="form-control" id="nw-von" name="von" value="${e(i.von ?? '')}"></div>
        <div><label class="form-label" for="nw-bis">Gültig bis</label><input type="date" class="form-control" id="nw-bis" name="bis" value="${e(i.bis ?? '')}"></div></div>
      <div class="form-check form-switch"><input class="form-check-input" type="checkbox" role="switch" id="nw-wichtig" name="wichtig" ${i.wichtig ? 'checked' : ''}><label class="form-check-label" for="nw-wichtig">Wichtig (oben, hervorgehoben)</label></div>
    </div>`,
    knoepfe: [{ text: 'Abbrechen' }, {
      text: 'Speichern', art: 'haupt', aktion: async box => {
        const f = n => box.querySelector(`[name=${n}]`);
        const daten = { titel: f('titel').value, text: f('text').value, zielgruppe: f('zielgruppe').value, von: f('von').value || null, bis: f('bis').value || null, wichtig: f('wichtig').checked };
        await senden(info ? `/api/app/infocenter/infos/${info.id}` : '/api/app/infocenter/infos', daten, info ? 'PUT' : 'POST');
        toast(info ? 'Info gespeichert.' : 'Info erstellt.');
        await laden();
        fertig();
      },
    }],
  });
}

export { beobachten };
