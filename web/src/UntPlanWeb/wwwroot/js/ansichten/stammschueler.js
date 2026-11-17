// Stammdaten → Schüler*innen → Klasse auswählen (Leitfaden Klassenleitung, Kapitel 5 und 6.1): Schülerliste der Klasse mit Bild.
// Einzelnen Schüler*innen ein Bild zuordnen: Stift vor dem Namen → „Bild hochladen“ → Datei auswählen → Import → Schließen → Speichern.
// Allen Schüler*innen der Klasse auf einmal: „Bildimport“ unter der Schülerliste mit einem ZIP-Archiv („Nachname, Vorname.jpg“).
// Dazu das Kennwort-Blatt (Anmeldeblätter, sechs Kärtchen je Seite) für die Erstanmeldung der Schüler*innen.
// Adresse: #/stammdaten/schueler/<Klasse>
import { e, holen, api, senden, toast, leer, herunterladen, initialen } from '../ui.js';
import { kbDialog as dialog, kbBestaetigen as bestaetigen } from '../kbdialog.js';
import { bilderNeu } from '../schuelerbilder.js';

const GESCHLECHT = { '': '', m: 'männlich', w: 'weiblich', d: 'divers' };
const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';
/** Bildadresse mit Zeitstempel (nach dem Hochladen neu laden). */
let stempel = Date.now();
export const bildUrl = id => `/api/app/schueler/${id}/bild?v=${stempel}`;
/** Bild bzw. Initialen einer Schülerin/eines Schülers. */
export const bildHtml = (x, mitBild, klasse = 'sb-bild') => mitBild
  ? `<img class="${klasse}" src="${bildUrl(x.id)}" alt="" loading="lazy">`
  : `<span class="${klasse} sb-initialen">${e(initialen(`${x.vorname} ${x.name}`))}</span>`;

export async function zeigen(seite, kontext, [, klasseParam]) {
  const alle = await holen('/api/app/schueler');
  const eigene = alle.klassen.filter(k => k.bearbeiten);
  const klasse = (klasseParam && klasseParam !== '-' ? klasseParam : '') || eigene[0]?.kurz || alle.klassen[0]?.kurz || '';
  const [d, bilder] = await Promise.all([holen(`/api/app/schueler?klasse=${encodeURIComponent(klasse)}`), holen('/api/app/schueler/bilder')]);
  const mitBild = new Set(bilder);
  const darf = d.klassen.find(k => k.kurz === klasse)?.bearbeiten ?? false;
  kontext.titel('Schüler*innen', `${darf ? '<button type="button" class="btn btn-outline-secondary" data-kennwortblatt><i class="bi bi-key"></i> Kennwort-Blatt</button>' : ''}
    <button type="button" class="btn btn-outline-secondary" data-csv><i class="bi bi-filetype-csv"></i> Bericht</button>`, String(d.schueler.length));
  seite.innerHTML = `<div class="nl-leiste">
      <label class="sg-feld"><span>Klasse</span><select class="form-select" data-klasse>${alle.klassen.map(k => `<option ${k.kurz === klasse ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select></label>
      <label class="nl-suche ms-auto"><i class="bi bi-search" aria-hidden="true"></i><input type="search" data-suche placeholder="Name suchen" aria-label="Name suchen"></label>
    </div>
    <div class="nl-tabelle sb-tabelle" data-inhalt></div>
    ${darf ? `<div class="sb-unter"><button type="button" class="nl-knopf" data-bildimport><i class="bi bi-images"></i>Bildimport</button></div>` : ''}`;
  seite.querySelector('[data-klasse]').onchange = ev => kontext.navigieren(`#/stammdaten/schueler/${encodeURIComponent(ev.target.value)}`);
  const inhalt = seite.querySelector('[data-inhalt]');
  const neu = () => { stempel = Date.now(); bilderNeu(); kontext.navigieren(location.hash); };
  inhalt.innerHTML = d.schueler.length ? `<table><thead><tr><th></th><th></th><th>Nachname</th><th>Vorname</th><th>Klasse</th><th>Geschlecht</th><th>Geburtsdatum</th><th>E-Mail</th></tr></thead><tbody>
    ${d.schueler.map(x => `<tr data-text="${e(`${x.name} ${x.vorname}`.toLowerCase())}">
      <td class="sb-stift">${x.bearbeiten ? `<button type="button" class="nl-icon sichtbar" data-bearbeiten="${x.id}" title="Bearbeiten" aria-label="${e(`${x.vorname} ${x.name}`)} bearbeiten"><i class="bi bi-pencil"></i></button>` : ''}</td>
      <td class="nl-avatar-zelle">${bildHtml(x, mitBild.has(x.id))}</td>
      <td class="ug-name">${e(x.name)}</td><td>${e(x.vorname)}</td><td>${e(x.klasse)}</td><td>${e(GESCHLECHT[x.geschlecht] ?? '')}</td>
      <td>${datumDe(x.geburtsdatum)}</td><td>${e(x.email)}</td></tr>`).join('')}</tbody></table>`
    : `<div class="p-3">${leer('Keine Schüler*innen in dieser Klasse.', 'people')}</div>`;
  seite.querySelector('[data-suche]').oninput = ev => {
    const t = ev.target.value.trim().toLowerCase();
    inhalt.querySelectorAll('tbody tr').forEach(tr => { tr.hidden = t.length > 0 && !tr.dataset.text.includes(t); });
  };
  inhalt.querySelectorAll('[data-bearbeiten]').forEach(b => b.onclick = () => bearbeiten(d.schueler.find(x => x.id === Number(b.dataset.bearbeiten)), mitBild.has(Number(b.dataset.bearbeiten)), neu));
  seite.querySelector('[data-bildimport]')?.addEventListener('click', () => bildimport(klasse, neu));
  document.querySelector('[data-kennwortblatt]')?.addEventListener('click', () => kennwortblatt(klasse));
  document.querySelector('[data-csv]').onclick = () => herunterladen(`schueler-${klasse}.csv`, ['Nachname;Vorname;Klasse;Geschlecht;Geburtsdatum;E-Mail;Bild',
    ...d.schueler.map(x => [x.name, x.vorname, x.klasse, x.geschlecht, x.geburtsdatum, x.email, mitBild.has(x.id) ? 'ja' : 'nein'].map(v => `"${String(v ?? '').replace(/"/g, '""')}"`).join(';'))].join('\r\n'));
}

/** Schüler*in bearbeiten: Bild (hochladen bzw. entfernen) und E-Mail-Adresse; „Speichern“ übernimmt beides. */
function bearbeiten(x, hatBild, nachher) {
  let neuesBild = null, entfernen = false;
  const box = dialog({
    titel: `${x.name}, ${x.vorname}`,
    inhalt: `<div class="sb-bearbeiten">
      <div class="sb-vorschau" data-vorschau>${bildHtml(x, hatBild, 'sb-gross')}</div>
      <div class="sb-felder">
        <div class="nl-unter">Klasse ${e(x.klasse)}</div>
        <div class="sb-knoepfe"><button type="button" class="nl-knopf" data-hochladen><i class="bi bi-upload"></i>Bild hochladen</button>
          <button type="button" class="nl-knopf rot" data-entfernen ${hatBild ? '' : 'hidden'}><i class="bi bi-trash"></i>Bild entfernen</button></div>
        <label class="sg-dfeld"><span>E-Mail-Adresse</span><input type="email" class="form-control" name="email" value="${e(x.email)}" maxlength="200"></label>
      </div></div>`,
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async () => {
      if (neuesBild) { const f = new FormData(); f.append('datei', neuesBild); await api(`/api/app/schueler/${x.id}/bild`, { methode: 'POST', formular: f }); }
      else if (entfernen) await api(`/api/app/schueler/${x.id}/bild`, { methode: 'DELETE' });
      const email = box.querySelector('[name=email]').value.trim();
      if (email !== x.email) {
        const k = await holen(`/api/app/schueler/${x.id}/kontaktdaten`);
        await senden(`/api/app/schueler/${x.id}/adresse`, { email, telefon: k.schueler.telefon, mobil: k.schueler.mobil }, 'PUT');
      }
      toast('Gespeichert.');
      nachher();
    } }],
  });
  const vorschau = box.querySelector('[data-vorschau]');
  box.querySelector('[data-entfernen]').onclick = () => { entfernen = true; neuesBild = null; vorschau.innerHTML = bildHtml(x, false, 'sb-gross'); box.querySelector('[data-entfernen]').hidden = true; };
  box.querySelector('[data-hochladen]').onclick = () => {
    // „Bild hochladen“: Datei auswählen, Import (Vorschau), Schließen
    let gewaehlt = null;
    const u = dialog({
      titel: 'Bild hochladen',
      inhalt: `<label class="sg-dfeld"><span>Datei auswählen</span><input type="file" class="form-control" accept="image/jpeg,image/png,image/gif,image/webp" data-datei></label>
        <div class="sb-import" data-ergebnis></div>`,
      knoepfe: [{ text: 'Import', art: 'haupt', aktion: () => {
        const datei = u.querySelector('[data-datei]').files[0];
        if (!datei) { toast('Bitte eine Datei auswählen.', true); return false; }
        if (datei.size > 2 * 1024 * 1024) { toast('Das Bild ist zu groß (höchstens 2 MB).', true); return false; }
        gewaehlt = datei;
        u.querySelector('[data-ergebnis]').innerHTML = `<img class="sb-gross" src="${URL.createObjectURL(datei)}" alt=""><span>${e(datei.name)} importiert.</span>`;
        return false;
      } }, { text: 'Schließen', aktion: () => {
        if (!gewaehlt) return;
        neuesBild = gewaehlt; entfernen = false;
        vorschau.innerHTML = `<img class="sb-gross" src="${URL.createObjectURL(gewaehlt)}" alt="">`;
        box.querySelector('[data-entfernen]').hidden = false;
      } }],
    });
  };
}

/** Bildimport: ZIP-Archiv mit „Nachname, Vorname.jpg“ für die ganze Klasse. */
function bildimport(klasse, nachher) {
  let importiert = false;
  const box = dialog({
    titel: `Bildimport ${klasse}`,
    inhalt: `<label class="sg-dfeld"><span>Datei auswählen</span><input type="file" class="form-control" accept=".zip,application/zip" data-datei></label>
      <div class="sb-import" data-ergebnis></div>`,
    knoepfe: [{ text: 'Import', art: 'haupt', aktion: async () => {
      const datei = box.querySelector('[data-datei]').files[0];
      if (!datei) { toast('Bitte ein ZIP-Archiv auswählen.', true); return false; }
      const f = new FormData();
      f.append('datei', datei);
      const r = await api(`/api/app/schueler/bildimport?klasse=${encodeURIComponent(klasse)}`, { methode: 'POST', formular: f });
      importiert = true;
      box.querySelector('[data-ergebnis]').innerHTML = `<p class="mb-1"><b>${r.importiert}</b> ${r.importiert === 1 ? 'Bild' : 'Bilder'} zugeordnet.</p>
        ${r.fehler.length ? `<p class="mb-1 text-danger">Nicht zugeordnet:</p><ul class="small">${r.fehler.map(x => `<li>${e(x)}</li>`).join('')}</ul>` : ''}
        ${r.ohneBild.length ? `<p class="mb-1">Noch ohne Bild: ${e(r.ohneBild.join('; '))}</p>` : '<p class="mb-0 text-success">Jede Schülerin und jeder Schüler hat jetzt ein Bild.</p>'}`;
      return false;
    } }, { text: 'Schließen' }],
  });
  box.geschlossen.then(() => { if (importiert) nachher(); });
}

/** Kennwort-Blatt: Schüler*innen wählen (vorgewählt: ohne Konto bzw. noch nie angemeldet), neue Startkennwörter, PDF mit sechs Kärtchen je Seite. */
export async function kennwortblatt(klasse) {
  const liste = await holen(`/api/app/kennwortblatt?klasse=${encodeURIComponent(klasse)}`);
  const box = dialog({
    titel: `Kennwort-Blatt ${klasse}`, breit: true,
    inhalt: `<div class="nl-tabelle"><table><thead><tr><th class="nl-wahl"><input type="checkbox" class="nl-rund" data-alle aria-label="Alle auswählen"></th><th>Name</th><th>Benutzername</th><th>Status</th></tr></thead><tbody>
      ${liste.map(x => `<tr><td class="nl-wahl"><input type="checkbox" class="nl-rund" value="${x.id}" ${!x.login || !x.angemeldet ? 'checked' : ''} aria-label="${e(`${x.vorname} ${x.name}`)}"></td>
        <td class="ug-name">${e(x.name)}, ${e(x.vorname)}</td><td>${e(x.login) || '<span class="nl-unter">wird angelegt</span>'}</td>
        <td>${!x.login ? '<span class="nl-chip grau">kein Konto</span>' : x.gesperrt ? '<span class="nl-chip rot">gesperrt</span>' : x.angemeldet ? '<span class="nl-chip gruen">schon angemeldet</span>' : '<span class="nl-chip gelb">noch nicht angemeldet</span>'}</td></tr>`).join('')}
      </tbody></table></div>`,
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Kennwort-Blatt erstellen (PDF)', art: 'haupt', aktion: async () => {
      const ids = [...box.querySelectorAll('tbody input:checked')].map(c => Number(c.value));
      if (!ids.length) { toast('Bitte mindestens eine Schülerin bzw. einen Schüler auswählen.', true); return false; }
      if (ids.some(id => liste.find(x => x.id === id)?.angemeldet) && !await bestaetigen('Kennwort-Blatt', 'Einige ausgewählte Schüler*innen haben sich schon angemeldet. Ihr Kennwort wird ebenfalls neu vergeben. Fortfahren?', 'Fortfahren')) return false;
      const r = await fetch('/api/app/kennwortblatt', { method: 'POST', credentials: 'same-origin', headers: { 'X-UntPlanWeb': '1', 'Content-Type': 'application/json' }, body: JSON.stringify({ klasse, schuelerIds: ids }) });
      if (!r.ok) throw new Error((await r.json().catch(() => null))?.fehler ?? 'Das Kennwort-Blatt konnte nicht erstellt werden.');
      herunterladen(`kennwortblatt-${klasse}.pdf`, await r.blob(), 'application/pdf');
      toast('Kennwort-Blatt erstellt.');
    } }],
  });
  box.querySelector('[data-alle]').onchange = ev => box.querySelectorAll('tbody input').forEach(c => { c.checked = ev.target.checked; });
}
