// Öffentliche Hinweise für die Anmeldeseite (#/hinweise; Administration bzw. Recht „Infocenter verwalten“) im nl-design:
// Titel, Text, Anzeigezeitraum und „anzeigen“ – nur allgemeine Informationen (z. B. „Elternsprechtag am …“). DSGVO: keine Namen,
// keine Termine einzelner Personen, keine Links und E-Mail-Adressen; die Anmeldeseite lädt die freigegebenen Hinweise ohne Cookies und ohne Tracking.
import { e, icon, holen, senden, api, toast, dialog, bestaetigen, leer, alarm, heuteIso, datumKurz } from '../ui.js';

const BASIS = '/api/app/hinweise';

function stileLaden() {
  if (document.querySelector('link[data-sprechtag-css]')) return;
  const l = document.createElement('link');
  l.rel = 'stylesheet';
  l.href = new URL('../../css/sprechtag.css', import.meta.url).href;
  l.dataset.sprechtagCss = '1';
  document.head.append(l);
}

/** Status eines Hinweises: angezeigt (heute im Zeitraum), geplant, abgelaufen bzw. aus. */
function status(h) {
  const heute = heuteIso();
  if (!h.aktiv) return ['grau', 'aus'];
  if (h.von && h.von > heute) return ['lila', `ab ${datumKurz(h.von)}`];
  if (h.bis && h.bis < heute) return ['grau', 'abgelaufen'];
  return ['gruen', 'wird angezeigt'];
}

export async function zeigen(seite, kontext) {
  stileLaden();
  const r = kontext.ich.rechte ?? {};
  if (!kontext.ich.admin && !r['infocenter.verwalten']) { seite.innerHTML = alarm('warning', 'Öffentliche Hinweise verwalten Administration und Schulleitung.'); return; }
  const neu = () => zeigen(seite, kontext);
  const liste = await holen(BASIS);
  kontext.titel('Hinweise auf der Anmeldeseite', `<button type="button" class="nl-hinzu" data-neu>${icon('plus')}Hinweis</button>`);
  seite.innerHTML = `<div class="nl-hinweis"><i class="bi bi-shield-check"></i><span>Diese Hinweise sind <b>ohne Anmeldung öffentlich</b> sichtbar. Bitte nur allgemeine
      Informationen eintragen (z. B. „Elternsprechtag am 12.11. von 15 bis 19 Uhr“) – keine Namen, keine Termine einzelner Personen, keine Links und
      keine E-Mail-Adressen. Die Anmeldeseite lädt die Hinweise ohne Cookies und ohne Tracking.</span></div>
    <div class="nl-tabelle">${liste.length ? `<table><thead><tr><th>Hinweis</th><th>Zeitraum</th><th>Status</th><th></th></tr></thead><tbody>
      ${liste.map(h => { const [farbe, text] = status(h); return `<tr>
        <td><div class="nl-name">${e(h.titel)}</div>${h.text ? `<div class="nl-unter st-hinweistext">${e(h.text)}</div>` : ''}</td>
        <td class="text-nowrap nl-unter">${h.von ? e(datumKurz(h.von)) : 'sofort'} – ${h.bis ? e(datumKurz(h.bis)) : 'offen'}</td>
        <td><span class="nl-chip ${farbe}">${e(text)}</span></td>
        <td class="text-end text-nowrap"><button type="button" class="nl-icon sichtbar" data-bearbeiten="${h.id}" title="Bearbeiten" aria-label="Bearbeiten"><i class="bi bi-pencil"></i></button>
          <button type="button" class="nl-icon sichtbar rot" data-loeschen="${h.id}" title="Löschen" aria-label="Löschen"><i class="bi bi-trash"></i></button></td></tr>`; }).join('')}
      </tbody></table>` : `<div class="p-4">${leer('Noch keine öffentlichen Hinweise.', 'megaphone')}</div>`}</div>`;

  const bearbeiten = h => dialog({
    titel: h ? 'Hinweis bearbeiten' : 'Neuer Hinweis',
    inhalt: `<div class="row g-3">
      <div class="col-12"><label class="form-label" for="h-titel">Titel</label><input class="form-control" id="h-titel" maxlength="120" value="${e(h?.titel ?? '')}" placeholder="z. B. Elternsprechtag" required></div>
      <div class="col-12"><label class="form-label" for="h-text">Text</label><textarea class="form-control" id="h-text" rows="4" maxlength="1000" placeholder="z. B. Am Do, 12.11. findet von 15 bis 19 Uhr der Elternsprechtag statt. Die Anmeldung beginnt am 02.11.">${e(h?.text ?? '')}</textarea>
        <div class="form-text">Nur Text – keine personenbezogenen Daten, keine Links.</div></div>
      <div class="col-sm-6"><label class="form-label" for="h-von">Anzeigen ab</label><input type="date" class="form-control" id="h-von" value="${e(h?.von ?? '')}"><div class="form-text">Leer = sofort.</div></div>
      <div class="col-sm-6"><label class="form-label" for="h-bis">Anzeigen bis</label><input type="date" class="form-control" id="h-bis" value="${e(h?.bis ?? '')}"><div class="form-text">Leer = ohne Ende.</div></div>
      <div class="col-12"><label class="st-schalter"><input type="checkbox" class="form-check-input" id="h-aktiv" ${h?.aktiv ?? true ? 'checked' : ''}><span>Auf der Anmeldeseite <b>anzeigen</b></span></label></div></div>`,
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
      const q = id => box.querySelector(id);
      await senden(h ? `${BASIS}/${h.id}` : BASIS, { titel: q('#h-titel').value, text: q('#h-text').value, von: q('#h-von').value, bis: q('#h-bis').value, aktiv: q('#h-aktiv').checked }, h ? 'PUT' : 'POST');
      toast('Hinweis gespeichert.');
      neu();
    } }],
  });
  document.querySelector('[data-neu]').onclick = () => bearbeiten(null);
  seite.querySelectorAll('[data-bearbeiten]').forEach(b => b.onclick = () => bearbeiten(liste.find(h => h.id === Number(b.dataset.bearbeiten))));
  seite.querySelectorAll('[data-loeschen]').forEach(b => b.onclick = async () => {
    const h = liste.find(x => x.id === Number(b.dataset.loeschen));
    if (!await bestaetigen('Hinweis löschen', `„${h.titel}“ löschen?`)) return;
    try { await api(`${BASIS}/${h.id}`, { methode: 'DELETE' }); toast('Hinweis gelöscht.'); neu(); } catch (err) { kontext.fehler(err); }
  });
}
