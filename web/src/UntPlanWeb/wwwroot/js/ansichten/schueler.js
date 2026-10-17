// Unterricht → Schüler*in (UW-06): Schülerverwaltung über UntPlanWeb. Liste je Klasse mit Suche; Administration und
// Schulleitung legen an, ändern (auch Klassenwechsel) und löschen, die Klassenleitung bearbeitet ihre Klassen, andere
// Lehrkräfte sehen nur Name, Klasse und Geschlecht. Die Liste aus UntPlan bleibt Grundlage; Änderungen überlagern sie.
import { e, icon, holen, senden, api, toast, dialog, bestaetigen, leer, herunterladen } from '../ui.js';

const GESCHLECHT = { '': '', m: 'männlich', w: 'weiblich', d: 'divers' };
const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';

export async function zeigen(seite, kontext, [klasseParam]) {
  const klasse = klasseParam && klasseParam !== '-' ? klasseParam : '';
  const d = await holen(`/api/app/schueler${klasse ? `?klasse=${encodeURIComponent(klasse)}` : ''}`);
  const klassenwahl = d.klassen.filter(k => d.loeschen || k.bearbeiten || true);
  kontext.titel('Schüler*innen', `${d.anlegen ? `<button class="btn btn-primary" data-neu>${icon('plus')}Neu</button>` : ''}<button class="btn btn-outline-secondary" data-csv>${icon('csv')}CSV</button>`, `${d.schueler.length}`);
  seite.innerHTML = `
    <div class="u-filter">
      <label class="u-feld"><span>Klasse</span><select class="form-select" data-klasse><option value="">- Alle -</option>${klassenwahl.map(k => `<option ${k.kurz === klasse ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select></label>
      <div class="u-rechts"><input type="search" class="form-control" data-suche placeholder="Name suchen" aria-label="Name suchen"></div>
    </div>
    <div class="u-tabelle-karte" data-inhalt></div>
    <p class="small text-body-secondary mt-2">${d.loeschen ? 'Sie können alle Schüler*innen bearbeiten.' : d.anlegen ? 'Sie können die Schüler*innen Ihrer Klassen bearbeiten; Klassenwechsel und Löschen nimmt die Administration vor.' : 'Bearbeiten können die Klassenleitung, die Schulleitung und die Administration.'}
      Die Schülerliste kommt aus UntPlan; Änderungen in UntPlanWeb bleiben bei der nächsten Übertragung erhalten.</p>`;
  const inhalt = seite.querySelector('[data-inhalt]');
  let filter = '';
  const zeichnen = () => {
    const l = d.schueler.filter(s => !filter || `${s.name} ${s.vorname} ${s.klasse}`.toLowerCase().includes(filter));
    inhalt.innerHTML = l.length ? `<table class="u-tabelle"><thead><tr><th>Nachname</th><th>Vorname</th><th>Klasse</th><th>Geschlecht</th><th>Geburtsdatum</th><th>E-Mail</th><th>Bemerkung</th><th>Quelle</th><th></th></tr></thead><tbody>
      ${l.map(s => `<tr><td class="fw-semibold">${e(s.name)}</td><td>${e(s.vorname)}</td><td>${e(s.klasse)}</td><td>${e(GESCHLECHT[s.geschlecht] ?? s.geschlecht)}</td>
        <td>${datumDe(s.geburtsdatum)}</td><td>${e(s.email)}</td><td class="small">${e(s.bemerkung)}</td><td class="small text-body-secondary">${e(s.quelle)}</td>
        <td class="text-end text-nowrap">${s.bearbeiten ? `<button class="btn btn-sm btn-outline-secondary" data-bearbeiten="${s.id}" title="Bearbeiten" aria-label="Bearbeiten">${icon('stift')}</button>` : ''}
          ${d.klassenwechsel ? `<button class="btn btn-sm btn-outline-secondary" data-wechsel="${s.id}" title="Klassenwechsel" aria-label="Klassenwechsel"><i class="bi bi-arrow-left-right" aria-hidden="true"></i></button>` : ''}
          ${d.loeschen ? `<button class="btn btn-sm btn-outline-danger" data-loeschen="${s.id}" title="Löschen" aria-label="Löschen">${icon('loeschen')}</button>` : ''}</td></tr>`).join('')}</tbody></table>`
      : leer('Keine Schüler*innen gefunden.', 'people');
    inhalt.querySelectorAll('[data-bearbeiten]').forEach(b => b.onclick = () => formular(d.schueler.find(s => s.id === Number(b.dataset.bearbeiten))));
    // Klassenwechsel ab Datum mit Verlauf (unterrichtklassen.js)
    inhalt.querySelectorAll('[data-wechsel]').forEach(b => b.onclick = async () => {
      try { (await import('./unterrichtklassen.js')).klassenwechselDialog(d.schueler.find(s => s.id === Number(b.dataset.wechsel)), d.klassen, () => kontext.navigieren(location.hash)); }
      catch (err) { kontext.fehler(err); }
    });
    inhalt.querySelectorAll('[data-loeschen]').forEach(b => b.onclick = async () => {
      const s = d.schueler.find(x => x.id === Number(b.dataset.loeschen));
      if (!await bestaetigen('Schüler*in löschen', `${s.name}, ${s.vorname} (${s.klasse}) aus UntPlanWeb entfernen?`)) return;
      try { await api(`/api/app/schueler/${s.id}`, { methode: 'DELETE' }); toast('Gelöscht.'); kontext.navigieren(location.hash); } catch (err) { kontext.fehler(err); }
    });
  };
  zeichnen();

  function formular(s) {
    const neu = !s;
    const darfKlasse = neu || d.klassenwechsel;
    const auswahl = d.klassen.filter(k => k.bearbeiten);
    dialog({
      titel: neu ? 'Schüler*in anlegen' : `${s.name}, ${s.vorname}`,
      inhalt: `<div class="row g-3">
        <div class="col-sm-6"><label class="form-label">Nachname</label><input class="form-control" name="name" value="${e(s?.name ?? '')}" maxlength="100" required></div>
        <div class="col-sm-6"><label class="form-label">Vorname</label><input class="form-control" name="vorname" value="${e(s?.vorname ?? '')}" maxlength="100"></div>
        <div class="col-sm-6"><label class="form-label">Klasse</label><select class="form-select" name="klasse" ${darfKlasse ? '' : 'disabled'}>${(darfKlasse ? auswahl : d.klassen).map(k => `<option ${k.kurz === (s?.klasse ?? klasse) ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select></div>
        <div class="col-sm-6"><label class="form-label">Geschlecht</label><select class="form-select" name="geschlecht">${Object.entries(GESCHLECHT).map(([k, t]) => `<option value="${k}" ${k === (s?.geschlecht ?? '') ? 'selected' : ''}>${t || '–'}</option>`).join('')}</select></div>
        <div class="col-sm-6"><label class="form-label">Geburtsdatum</label><input type="date" class="form-control" name="geburtsdatum" value="${e(s?.geburtsdatum ?? '')}"></div>
        <div class="col-sm-6"><label class="form-label">E-Mail</label><input type="email" class="form-control" name="email" value="${e(s?.email ?? '')}" maxlength="200"></div>
        <div class="col-12"><label class="form-label">Bemerkung</label><textarea class="form-control" name="bemerkung" rows="2" maxlength="1000">${e(s?.bemerkung ?? '')}</textarea></div></div>`,
      knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
        const w = n => box.querySelector(`[name=${n}]`).value;
        const daten = { name: w('name'), vorname: w('vorname'), klasse: w('klasse'), geschlecht: w('geschlecht'), geburtsdatum: w('geburtsdatum'), email: w('email'), bemerkung: w('bemerkung') };
        if (neu) await senden('/api/app/schueler', daten); else await senden(`/api/app/schueler/${s.id}`, daten, 'PUT');
        toast('Gespeichert.');
        kontext.navigieren(location.hash);
      } }],
    });
  }
  document.querySelector('[data-neu]')?.addEventListener('click', () => formular(null));
  seite.querySelector('[data-klasse]').onchange = ev => kontext.navigieren(`#/schueler/${encodeURIComponent(ev.target.value || '-')}`);
  seite.querySelector('[data-suche]').oninput = ev => { filter = ev.target.value.trim().toLowerCase(); zeichnen(); };
  document.querySelector('[data-csv]').onclick = () => herunterladen(`schueler-${klasse || 'alle'}.csv`,
    ['Nachname;Vorname;Klasse;Geschlecht;Geburtsdatum;E-Mail', ...d.schueler.map(s => [s.name, s.vorname, s.klasse, s.geschlecht, s.geburtsdatum, s.email].map(x => `"${String(x ?? '').replace(/"/g, '""')}"`).join(';'))].join('\r\n'));
}
