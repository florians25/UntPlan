// Kontaktdaten der Schüler*innen (Leitfaden Klassenleitung, Kapitel 6.2/6.3): Klassenleitungen sehen, welche Daten über die
// Schüler*innen, ihre Erziehungsberechtigten und Ausbildungsbeauftragten hinterlegt sind – Geburtsdatum, eigene Adresse (Telefonnummer,
// Mobiltelefon, E-Mail-Adresse) und je Kontakt Telefon, Mobil und E-Mail mit Kopiersymbol. Fehlt eine E-Mail-Adresse oder ist sie falsch,
// korrigiert die Klassenleitung sie hier; über diese Adressen registrieren sich Eltern und Betriebe selbst. Das @-Symbol im Sitzplan
// öffnet den Dialog „Adresse“ (adresseDialog).
// Adresse: #/kontaktdaten/<Klasse>/<Schüler-Id>/<Personenrolle | ->
import { e, holen, senden, api, toast, leer } from '../ui.js';
import { kbDialog as dialog, kbBestaetigen as bestaetigen } from '../kbdialog.js';
import { bildHtml } from './stammschueler.js';

const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';
const ROLLEN = { erziehungsberechtigt: 'Erziehungsberechtigte*r', ausbildungsbeauftragt: 'Ausbildungsbeauftragte*r' };
const PERSONENROLLEN = [['', 'Personenrolle'], ['eigene', 'Eigene Adresse'], ['erziehungsberechtigt', 'Erziehungsberechtigte'], ['ausbildungsbeauftragt', 'Ausbildungsbeauftragte']];

const kopierbar = wert => wert
  ? `<span class="kt-wert">${e(wert)}<button type="button" class="kt-kopieren" data-kopieren="${e(wert)}" title="Kopieren" aria-label="${e(wert)} kopieren"><i class="bi bi-copy"></i></button></span>`
  : '<span class="kt-wert">---</span>';
const feld = (label, wert) => `<div class="kt-feld"><span class="kt-label">${e(label)}</span>${kopierbar(wert)}</div>`;

function kopierenBinden(wurzel) {
  wurzel.querySelectorAll('[data-kopieren]').forEach(b => b.onclick = async () => {
    try { await navigator.clipboard.writeText(b.dataset.kopieren); toast('Kopiert.'); } catch { toast('Kopieren ist in diesem Browser nicht möglich.', true); }
  });
}

export async function zeigen(seite, kontext, [klasseParam, schuelerParam, rolleParam]) {
  const alle = await holen('/api/app/schueler');
  const klassen = alle.klassen.filter(k => k.bearbeiten);
  const klasse = (klasseParam && klasseParam !== '-' ? klasseParam : '') || klassen[0]?.kurz || '';
  const rolle = PERSONENROLLEN.some(([k]) => k && k === rolleParam) ? rolleParam : '';
  const ziel = (k, s, r = rolle) => `#/kontaktdaten/${encodeURIComponent(k || '-')}/${s || '-'}/${r || '-'}`;
  kontext.titel('Kontaktdaten');
  if (!klassen.length) { seite.innerHTML = `<div class="p-3">${leer('Kontaktdaten sehen die Klassenleitungen, die Schulleitung und die Administration.', 'people')}</div>`; return; }
  const d = await holen(`/api/app/schueler?klasse=${encodeURIComponent(klasse)}`);
  const sid = Number(schuelerParam) || d.schueler[0]?.id || 0;
  seite.innerHTML = `<div class="kt-seite">
    <div class="nl-leiste">
      <label class="sg-feld"><span>Klasse</span><select class="form-select" data-klasse>${klassen.map(k => `<option ${k.kurz === klasse ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select></label>
      <label class="sg-feld kt-schuelerwahl"><span>Schüler*in</span><select class="form-select" data-schueler>${d.schueler.map(x => `<option value="${x.id}" ${x.id === sid ? 'selected' : ''}>${e(x.name)} ${e(x.vorname)}</option>`).join('')}</select></label>
      <label class="sg-feld"><span>Personenrolle</span><select class="form-select" data-rolle>${PERSONENROLLEN.map(([k, t]) => `<option value="${k}" ${k === rolle ? 'selected' : ''}>${k ? t : '- Alle -'}</option>`).join('')}</select></label>
    </div>
    <div data-inhalt></div></div>`;
  seite.querySelector('[data-klasse]').onchange = ev => kontext.navigieren(ziel(ev.target.value, null));
  seite.querySelector('[data-schueler]').onchange = ev => kontext.navigieren(ziel(klasse, ev.target.value));
  seite.querySelector('[data-rolle]').onchange = ev => kontext.navigieren(ziel(klasse, sid, ev.target.value));
  const inhalt = seite.querySelector('[data-inhalt]');
  if (!sid) { inhalt.innerHTML = `<div class="p-3">${leer('Keine Schüler*innen in dieser Klasse.', 'people')}</div>`; return; }
  const k = await holen(`/api/app/schueler/${sid}/kontaktdaten`);
  const x = k.schueler;
  const neu = () => kontext.navigieren(location.hash);
  const zeigt = r => !rolle || rolle === r;
  inhalt.innerHTML = `<div class="kt-karte">
    <div class="kt-kopf"><div><span class="kt-label">Geburtsdatum</span><div>${datumDe(x.geburtsdatum) || '---'}</div></div>${bildHtml(x, x.bild, 'kt-bild')}</div>
    ${zeigt('eigene') ? `<section class="kt-abschnitt"><h3>Eigene Adresse <button type="button" class="nl-icon sichtbar" data-adresse title="Bearbeiten" aria-label="Eigene Adresse bearbeiten"><i class="bi bi-pencil"></i></button></h3>
      <div class="kt-felder">${feld('Telefonnummer', x.telefon)}${feld('Mobiltelefon', x.mobil)}${feld('E-Mail Adresse', x.email)}</div></section>` : ''}
    ${k.kontakte.filter(c => zeigt(c.rolle)).map(c => `<section class="kt-abschnitt">
      <h3>${c.rolle === 'ausbildungsbeauftragt' ? `Ausbildungsbeauftragte: ${e([c.betrieb, c.name].filter(Boolean).join(' '))}` : `Erziehungsberechtigter: ${e(c.name)}`}
        <button type="button" class="nl-icon sichtbar" data-kontakt="${c.id}" title="Bearbeiten" aria-label="Kontakt bearbeiten"><i class="bi bi-pencil"></i></button>
        <button type="button" class="nl-icon sichtbar rot" data-loeschen="${c.id}" title="Löschen" aria-label="Kontakt löschen"><i class="bi bi-trash"></i></button></h3>
      <div class="kt-felder">${feld('Telefonnummer', c.telefon)}${feld('Mobiltelefon', c.mobil)}${feld('E-Mail Adresse', c.email)}</div></section>`).join('')}
    ${zeigt('erziehungsberechtigt') ? (k.personen ?? []).map(p => `<section class="kt-abschnitt">
      <h3>Erziehungsberechtigter: ${e(p.name)} <span class="nl-chip grau" title="Gepflegt unter Administration → Personen & Konten">Personen & Konten</span></h3>
      <div class="kt-felder">${feld('Telefonnummer', p.telefon)}${feld('Mobiltelefon', '')}${feld('E-Mail Adresse', p.email)}</div></section>`).join('') : ''}
    <div class="kt-fuss"><button type="button" class="nl-knopf" data-neu="erziehungsberechtigt"><i class="bi bi-plus-lg"></i>Erziehungsberechtigte*n hinzufügen</button>
      <button type="button" class="nl-knopf" data-neu="ausbildungsbeauftragt"><i class="bi bi-plus-lg"></i>Ausbildungsbeauftragte*n hinzufügen</button></div></div>`;
  kopierenBinden(inhalt);
  inhalt.querySelector('[data-adresse]')?.addEventListener('click', () => adresseDialog(sid, `${x.name} ${x.vorname}`, neu));
  inhalt.querySelectorAll('[data-kontakt]').forEach(b => b.onclick = () => kontaktDialog(sid, k.kontakte.find(c => c.id === Number(b.dataset.kontakt)), null, neu));
  inhalt.querySelectorAll('[data-neu]').forEach(b => b.onclick = () => kontaktDialog(sid, null, b.dataset.neu, neu));
  inhalt.querySelectorAll('[data-loeschen]').forEach(b => b.onclick = async () => {
    if (!await bestaetigen('Kontakt löschen', 'Diesen Kontakt löschen?')) return;
    try { await api(`/api/app/schueler/${sid}/kontakte/${b.dataset.loeschen}`, { methode: 'DELETE' }); toast('Gelöscht.'); neu(); } catch (err) { toast(err.message, true); }
  });
}

function kontaktDialog(sid, c, rolle, nachher) {
  const r = c?.rolle ?? rolle;
  dialog({
    titel: c ? ROLLEN[r] : `${ROLLEN[r]} hinzufügen`,
    inhalt: `<div class="sg-formular">
      <label class="sg-dfeld"><span>Personenrolle</span><select class="form-select" name="rolle">${Object.entries(ROLLEN).map(([k, t]) => `<option value="${k}" ${k === r ? 'selected' : ''}>${t}</option>`).join('')}</select></label>
      <label class="sg-dfeld"><span>Name</span><input class="form-control" name="name" maxlength="200" value="${e(c?.name ?? '')}"></label>
      <label class="sg-dfeld"><span>Betrieb / Einrichtung</span><input class="form-control" name="betrieb" maxlength="200" value="${e(c?.betrieb ?? '')}"></label>
      <div class="sg-reihe"><label class="sg-dfeld"><span>Telefonnummer</span><input class="form-control" name="telefon" maxlength="50" value="${e(c?.telefon ?? '')}"></label>
        <label class="sg-dfeld"><span>Mobiltelefon</span><input class="form-control" name="mobil" maxlength="50" value="${e(c?.mobil ?? '')}"></label></div>
      <label class="sg-dfeld"><span>E-Mail Adresse</span><input type="email" class="form-control" name="email" maxlength="200" value="${e(c?.email ?? '')}"></label></div>`,
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
      const w = n => box.querySelector(`[name=${n}]`).value;
      const daten = { rolle: w('rolle'), name: w('name'), betrieb: w('betrieb'), telefon: w('telefon'), mobil: w('mobil'), email: w('email') };
      if (c) await senden(`/api/app/schueler/${sid}/kontakte/${c.id}`, daten, 'PUT'); else await senden(`/api/app/schueler/${sid}/kontakte`, daten);
      toast('Gespeichert.');
      nachher?.();
    } }],
  });
}

/**
 * Dialog „Adresse“ (z. B. über das @-Symbol im Sitzplan): E-Mail-Adresse, Telefonnummer und Mobiltelefon der Schülerin/des Schülers;
 * darunter die Kontakte. Die Klassenleitung korrigiert eine fehlende bzw. falsche E-Mail-Adresse und meldet die Korrektur dem Sekretariat.
 */
export async function adresseDialog(sid, name, nachher) {
  const k = await holen(`/api/app/schueler/${sid}/kontaktdaten`);
  const x = k.schueler;
  const box = dialog({
    titel: 'Adresse',
    inhalt: `<div class="kt-adresse">
      <div class="kt-adresse-name">${e(name || `${x.name} ${x.vorname}`)}</div>
      <label class="sg-dfeld"><span>E-Mail Adresse</span><input type="email" class="form-control" name="email" maxlength="200" value="${e(x.email)}"></label>
      <div class="sg-reihe"><label class="sg-dfeld"><span>Telefonnummer</span><input class="form-control" name="telefon" maxlength="50" value="${e(x.telefon)}"></label>
        <label class="sg-dfeld"><span>Mobiltelefon</span><input class="form-control" name="mobil" maxlength="50" value="${e(x.mobil)}"></label></div>
      ${k.kontakte.length ? `<div class="kt-adresse-kontakte">${k.kontakte.map(c => `<div><span class="kt-label">${e(ROLLEN[c.rolle])}: ${e([c.betrieb, c.name].filter(Boolean).join(' '))}</span>${kopierbar(c.email)}</div>`).join('')}</div>` : ''}</div>`,
    knoepfe: [{ text: 'Schließen' }, { text: 'Speichern', art: 'haupt', aktion: async b => {
      const w = n => b.querySelector(`[name=${n}]`).value;
      await senden(`/api/app/schueler/${sid}/adresse`, { email: w('email'), telefon: w('telefon'), mobil: w('mobil') }, 'PUT');
      toast('Adresse gespeichert.');
      nachher?.();
    } }],
  });
  kopierenBinden(box);
}
