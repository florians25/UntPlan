// Stammdaten (Administration): Klassen, Schüler*innen, Lehrkräfte, Fächer und Räume aus UntPlan (nur lesen – gepflegt wird in UntPlan),
// Stundenplan-Versionen (Übertragungsverlauf) sowie die in UntPlanWeb gepflegten Abwesenheits-/Befreiungsgründe und Klassendienste;
// Notenschemen und Prüfungsarten zeigt noten.js.
import { e, icon, holen, senden, api, toast, dialog, bestaetigen, leer, herunterladen, suchfeldMitFilter, suchfeldMitFilterBinden } from '../ui.js';

const ARTEN = {
  klassen: { titel: 'Klassen', spalten: [['kurz', 'Klasse'], ['name', 'Langname'], ['jahrgang', 'Jahrgang'], ['leitung', 'Klassenleitung'], ['schueler', 'Schüler*innen']] },
  schueler: { titel: 'Schüler*innen', spalten: [['name', 'Nachname'], ['vorname', 'Vorname'], ['klasse', 'Klasse'], ['id', 'Nummer']] },
  lehrer: { titel: 'Lehrkräfte', spalten: [['kurz', 'Kürzel'], ['name', 'Nachname'], ['vorname', 'Vorname'], ['email', 'E-Mail']] },
  faecher: { titel: 'Fächer', spalten: [['kurz', 'Fach'], ['name', 'Langname'], ['farbe', 'Farbe']] },
  raeume: { titel: 'Räume', spalten: [['kurz', 'Raum'], ['name', 'Langname'], ['buchbar', 'buchbar (Web)']] },
  versionen: { titel: 'Stundenplan-Versionen', spalten: [['revision', 'Revision'], ['empfangen', 'Empfangen'], ['programmversion', 'UntPlan'], ['klassen', 'Klassen'], ['lehrer', 'Lehrkräfte'], ['raeume', 'Räume'], ['tage', 'Tage'], ['vertretungen', 'Vertretungen'], ['von', 'Von'], ['bis', 'Bis']] },
  gruende: { titel: 'Abwesenheitsgründe', spalten: [['kurz', 'Kürzel'], ['name', 'Bezeichnung'], ['art', 'Art'], ['entschuldigt', 'gilt als entschuldigt'], ['reihenfolge', 'Reihenfolge'], ['aktiv', 'aktiv']] },
  dienste: { titel: 'Dienste', spalten: [['kurz', 'Kürzel'], ['name', 'Bezeichnung'], ['reihenfolge', 'Reihenfolge'], ['aktiv', 'aktiv']] },
};

// Filter-Dropdowns je Bereich im Suchfeld (Werte aus den geladenen Daten)
const FILTER = {
  schueler: [['klasse', 'Klasse', 'Alle Klassen']],
  klassen: [['jahrgang', 'Jahrgang', 'Alle Jahrgänge']],
  raeume: [['buchbar', 'Buchbar', 'Alle Räume', { true: 'buchbar', false: 'nicht buchbar' }]],
  gruende: [['art', 'Art', 'Alle Arten', { abwesenheit: 'Abwesenheit', befreiung: 'Befreiung' }], ['aktiv', 'Aktiv', 'Aktiv und inaktiv', { true: 'aktiv', false: 'inaktiv' }]],
  dienste: [['aktiv', 'Aktiv', 'Aktiv und inaktiv', { true: 'aktiv', false: 'inaktiv' }]],
};
const filterFuer = (art, liste) => (FILTER[art] ?? []).map(([k, label, alle, texte]) => ({ name: k, label, alle,
  optionen: [...new Set(liste.map(x => String(x[k] ?? '')).filter(Boolean))].sort((a, b) => a.localeCompare(b, 'de', { numeric: true })).map(w => [w, texte?.[w] ?? w]) }));
const filterWerte = (art, x) => (FILTER[art] ?? []).map(([k]) => `${k}=${x[k] ?? ''}`).join('|');

const anzeige = (schluessel, wert) => {
  if (typeof wert === 'boolean') return wert ? '<i class="bi bi-check-lg text-success"></i>' : '';
  if (schluessel === 'farbe' && /^#[0-9a-f]{6}$/i.test(wert)) return `<span class="fachpunkt" style="background:${wert}"></span>${e(wert)}`;
  if (schluessel === 'art') return wert === 'befreiung' ? 'Befreiung' : 'Abwesenheit';
  if ((schluessel === 'von' || schluessel === 'bis') && /^\d{4}-/.test(wert)) return e(wert.split('-').reverse().join('.'));
  return e(wert ?? '');
};

export async function zeigen(seite, kontext, [artParam]) {
  // Notenschemen und Prüfungsarten (in UntPlanWeb gepflegt) – eigene Seiten in noten.js
  if (artParam === 'notenschemen' || artParam === 'pruefungsarten') return (await import('./noten.js')).zeigen(seite, kontext, artParam);
  const art = ARTEN[artParam] ? artParam : 'klassen';
  const a = ARTEN[art];
  const bearbeitbar = art === 'gruende' || art === 'dienste';
  const liste = await holen(`/api/app/stammdaten/${art}`);
  kontext.titel(a.titel, `${bearbeitbar ? `<button class="btn btn-primary" data-neu>${icon('plus')}Neu</button>` : ''}<button class="btn btn-outline-secondary" data-csv>${icon('csv')}Bericht</button>`, String(liste.length));
  seite.innerHTML = `
    ${!bearbeitbar && art !== 'versionen' ? '<p class="small text-body-secondary">Diese Stammdaten werden in UntPlan gepflegt und mit jeder Übertragung aktualisiert.</p>' : ''}
    ${art === 'versionen' ? '<p class="small text-body-secondary">Jede Übertragung aus UntPlan erzeugt eine neue Version des Stundenplans; aktiv ist immer die zuletzt empfangene.</p>' : ''}
    <div class="u-sf-leiste">${suchfeldMitFilter({ platzhalter: 'Suchen', label: `${a.titel} durchsuchen`, filter: filterFuer(art, liste) })}</div>
    <div class="u-tabelle-karte">${liste.length ? `<table class="u-tabelle"><thead><tr>${a.spalten.map(([, t]) => `<th>${e(t)}</th>`).join('')}${bearbeitbar ? '<th></th>' : ''}</tr></thead><tbody>
      ${liste.map((x, i) => `<tr data-zeile="${i}" data-filter="${e(filterWerte(art, x))}" data-text="${e(Object.values(x).join(' ').toLowerCase())}">${a.spalten.map(([k], j) => `<td class="${j === 0 ? 'fw-semibold' : ''}" style="${j === 0 ? 'width:auto' : ''}">${anzeige(k, x[k])}${art === 'versionen' && j === 0 && i === 0 ? ' <span class="u-chip gruen ms-1">aktiv</span>' : ''}</td>`).join('')}
        ${bearbeitbar ? `<td class="text-end text-nowrap"><button class="btn btn-sm btn-outline-secondary" data-bearbeiten="${i}" title="Bearbeiten">${icon('stift')}</button>
          <button class="btn btn-sm btn-outline-danger" data-loeschen="${i}" title="Löschen">${icon('loeschen')}</button></td>` : ''}</tr>`).join('')}
      </tbody></table>` : leer('Keine Einträge vorhanden.')}</div>`;
  // gemeinsames Suchfeld mit Filter: Freitext über alle Spalten, je nach Bereich ein passender Filter (Klasse, Jahrgang, Art …)
  let text = '';
  suchfeldMitFilterBinden(seite, { beiSuche: t => { text = t.toLowerCase(); filtern(); }, beiFilter: () => filtern() });
  const filtern = () => {
    const wahl = [...seite.querySelectorAll('select[data-sf]')].filter(x => x.value).map(x => `${x.dataset.sf}=${x.value}`);
    seite.querySelectorAll('[data-zeile]').forEach(z => {
      const werte = z.dataset.filter.split('|');
      z.classList.toggle('d-none', (!!text && !z.dataset.text.includes(text)) || !wahl.every(w => werte.includes(w)));
    });
  };
  document.querySelector('[data-csv]').onclick = () => herunterladen(`${art}.csv`, [a.spalten.map(([, t]) => t).join(';'),
    ...liste.map(x => a.spalten.map(([k]) => `"${String(typeof x[k] === 'boolean' ? (x[k] ? 'ja' : 'nein') : x[k] ?? '').replace(/"/g, '""')}"`).join(';'))].join('\r\n'));
  if (!bearbeitbar) return;
  const neu = () => kontext.navigieren(location.hash);
  const pfad = art === 'gruende' ? '/api/app/admin/gruende' : '/api/app/admin/dienstarten';
  const formular = x => dialog({
    titel: x ? `${a.titel}: ${x.name}` : `Neu: ${a.titel}`,
    inhalt: `<div class="row g-3">
      <div class="col-sm-4"><label class="form-label">Kürzel</label><input class="form-control" name="kurz" value="${e(x?.kurz ?? '')}" maxlength="10"></div>
      <div class="col-sm-8"><label class="form-label">Bezeichnung</label><input class="form-control" name="name" value="${e(x?.name ?? '')}" required></div>
      ${art === 'gruende' ? `<div class="col-sm-6"><label class="form-label">Art</label><select class="form-select" name="art"><option value="abwesenheit">Abwesenheit</option><option value="befreiung" ${x?.art === 'befreiung' ? 'selected' : ''}>Befreiung</option></select></div>
        <div class="col-sm-6 d-flex align-items-end"><div class="form-check"><input class="form-check-input" type="checkbox" name="entschuldigt" id="sd-e" ${x?.entschuldigt ? 'checked' : ''}><label class="form-check-label" for="sd-e">gilt als entschuldigt</label></div></div>` : ''}
      <div class="col-sm-6"><label class="form-label">Reihenfolge</label><input type="number" class="form-control" name="reihenfolge" value="${x?.reihenfolge ?? 10}"></div>
      <div class="col-sm-6 d-flex align-items-end"><div class="form-check"><input class="form-check-input" type="checkbox" name="aktiv" id="sd-a" ${x?.aktiv ?? true ? 'checked' : ''}><label class="form-check-label" for="sd-a">aktiv</label></div></div></div>`,
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
      const v = n => box.querySelector(`[name=${n}]`);
      await senden(x ? `${pfad}/${x.id}` : pfad, { kurz: v('kurz').value, name: v('name').value, art: v('art')?.value, entschuldigt: !!v('entschuldigt')?.checked, reihenfolge: Number(v('reihenfolge').value), aktiv: v('aktiv').checked }, x ? 'PUT' : 'POST');
      toast('Gespeichert.'); neu();
    } }],
  });
  document.querySelector('[data-neu]').onclick = () => formular(null);
  seite.querySelectorAll('[data-bearbeiten]').forEach(b => b.onclick = () => formular(liste[Number(b.dataset.bearbeiten)]));
  seite.querySelectorAll('[data-loeschen]').forEach(b => b.onclick = async () => {
    const x = liste[Number(b.dataset.loeschen)];
    if (!await bestaetigen(`${a.titel} löschen`, `„${x.name}“ löschen?${art === 'dienste' ? ' Alle Einteilungen dieses Dienstes werden ebenfalls gelöscht.' : ''}`)) return;
    await api(`${pfad}/${x.id}`, { methode: 'DELETE' });
    neu();
  });
}
