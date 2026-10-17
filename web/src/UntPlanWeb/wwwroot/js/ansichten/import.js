// Administration → Import → Schüler*innen: CSV/TXT hochladen (Trennzeichen und Kodierung automatisch), Vorschau, Spaltenzuordnung mit
// Vorlagen, Abgleich-Modus, Trockenlauf mit Ergebnisliste und Übernahme (optional mit Konten). Dazu die Quelle der Schülerdaten.
import { e, icon, holen, senden, api, toast, dialog, bestaetigen, alarm, heuteIso, herunterladen } from '../ui.js';

const STATUS = { neu: ['gruen', 'neu'], geaendert: ['orange', 'geändert'], unveraendert: ['', 'unverändert'], uebersprungen: ['', 'übersprungen'], fehler: ['rot', 'Fehler'], ausgetreten: ['lila', 'ausgetreten'] };
const QUELLE = { export: 'UntPlan-Export', web: 'Web-Verwaltung' };

function stil() {
  if (document.getElementById('import-stil')) return;
  const s = document.createElement('style');
  s.id = 'import-stil';
  s.textContent = `
    .si-schritt { display: flex; align-items: center; gap: .5rem; font-weight: 700; margin-bottom: .5rem; }
    .si-schritt b { display: inline-flex; width: 1.6rem; height: 1.6rem; border-radius: 50%; align-items: center; justify-content: center; background: var(--u-akzent, #f26b38); color: #fff; font-size: .85rem; }
    .si-tabelle { font-size: .8rem; }
    .si-tabelle th { white-space: nowrap; vertical-align: bottom; }
    .si-tabelle th select { min-width: 10rem; font-size: .78rem; margin-top: .25rem; }
    .si-tabelle td { white-space: nowrap; max-width: 16rem; overflow: hidden; text-overflow: ellipsis; }
    .si-tabelle tr.kopf td { color: var(--u-grau-2, #6c757d); font-style: italic; }
    .si-zeile-fehler td { background: #fdecec !important; }
    .si-aenderung { font-size: .75rem; color: var(--u-grau-2, #6c757d); }
  `;
  document.head.append(s);
}

/** Karte „Quelle der Schülerdaten“ (auch unter Administration → Ansichtseinstellungen). */
export async function quelleKarte(box, nachher) {
  const q = await holen('/api/app/admin/schuelerimport/quelle');
  box.innerHTML = `<div class="card u-einstellungen mb-3"><div class="card-header">Quelle der Schülerdaten</div><div class="card-body">
    ${Object.entries(QUELLE).map(([id, t]) => `<div class="form-check"><input class="form-check-input" type="radio" name="si-quelle" id="si-q-${id}" value="${id}" ${q.quelle === id ? 'checked' : ''}>
      <label class="form-check-label" for="si-q-${id}"><b>${t}</b> – ${id === 'export' ? 'Schüler*innen kommen mit jeder Übertragung aus der Desktop-App; Änderungen in UntPlanWeb ergänzen sie.' : 'Schüler*innen werden in UntPlanWeb gepflegt (Schülerverwaltung, CSV-Import); der Export überschreibt sie nicht.'}</label></div>`).join('')}
    <p class="small text-body-secondary mt-2 mb-0">Beim Wechsel zur Web-Verwaltung werden die bisher übertragenen Schüler*innen übernommen.</p></div></div>`;
  box.querySelectorAll('[name=si-quelle]').forEach(r => r.onchange = async () => {
    if (!await bestaetigen('Quelle der Schülerdaten', `Quelle auf „${QUELLE[r.value]}“ umstellen?`, 'Umstellen')) { box.querySelector(`#si-q-${q.quelle}`).checked = true; return; }
    try {
      const n = await senden('/api/app/admin/schuelerimport/quelle', { quelle: r.value }, 'PUT');
      q.quelle = n.quelle;
      toast(n.uebernommen ? `Umgestellt – ${n.uebernommen} Schüler*innen übernommen.` : 'Umgestellt.');
      nachher?.(n.quelle);
    } catch (err) { toast(err.message, true); box.querySelector(`#si-q-${q.quelle}`).checked = true; }
  });
}

export async function zeigen(ziel, kontext) {
  stil();
  const g = await holen('/api/app/admin/schuelerimport/grundlagen');
  let datei = null;       // Ergebnis von /lesen
  let zuordnung = {};     // Spaltennummer → Feld
  let kopfzeile = true;
  ziel.innerHTML = `<div data-quelle></div>
    <div class="card mb-3"><div class="card-body">
      <div class="si-schritt"><b>1</b>Datei wählen</div>
      <p class="small text-body-secondary">CSV- oder TXT-Datei (Trennzeichen ; , oder Tab, UTF-8 oder Windows-1252 – beides wird erkannt). Aktuell ${g.schueler} Schüler*innen.</p>
      <div class="d-flex flex-wrap gap-2 align-items-end">
        <label class="btn btn-outline-secondary mb-0">${icon('hochladen')}Datei wählen<input type="file" accept=".csv,.txt,text/csv,text/plain" data-datei hidden></label>
        <label class="small">Trennzeichen<select class="form-select form-select-sm" data-trenner><option value="">automatisch</option><option value=";">Semikolon</option><option value=",">Komma</option><option value="tab">Tabulator</option></select></label>
        <label class="small">Kodierung<select class="form-select form-select-sm" data-kodierung><option value="">automatisch</option><option value="utf-8">UTF-8</option><option value="windows-1252">Windows-1252</option></select></label>
        <span class="small text-body-secondary" data-info></span>
      </div></div></div>
    <div data-zuordnung></div><div data-ergebnis></div>`;
  if (g.darfQuelle) await quelleKarte(ziel.querySelector('[data-quelle]'), q => { g.quelle = q; });
  else ziel.querySelector('[data-quelle]').innerHTML = alarm('info', `Quelle der Schülerdaten: ${QUELLE[g.quelle]}.`);

  const felderOptionen = sel => `<option value="">– nicht übernehmen –</option>${[...new Set(g.felder.map(f => f.gruppe))].map(gr => `<optgroup label="${e(gr)}">${g.felder.filter(f => f.gruppe === gr)
    .map(f => `<option value="${f.id}" ${sel === f.id ? 'selected' : ''}>${e(f.name)}</option>`).join('')}</optgroup>`).join('')}`;

  const laden = async () => {
    const input = ziel.querySelector('[data-datei]');
    const f = input.files[0];
    if (!f) return;
    const fd = new FormData();
    fd.append('datei', f);
    fd.append('trennzeichen', ziel.querySelector('[data-trenner]').value);
    fd.append('kodierung', ziel.querySelector('[data-kodierung]').value);
    try {
      datei = await api('/api/app/admin/schuelerimport/lesen', { methode: 'POST', formular: fd });
      kopfzeile = datei.kopfzeile;
      zuordnung = { ...datei.vorschlag };
      ziel.querySelector('[data-info]').textContent = `${datei.datei}: ${datei.anzahl} Zeilen · ${datei.kodierung} · Trennzeichen ${datei.trennzeichen === 'tab' ? 'Tabulator' : `„${datei.trennzeichen}“`}`;
      ziel.querySelector('[data-ergebnis]').innerHTML = '';
      zuordnen();
    } catch (err) { kontext.fehler(err); }
  };
  ziel.querySelector('[data-datei]').onchange = laden;
  ziel.querySelector('[data-trenner]').onchange = laden;
  ziel.querySelector('[data-kodierung]').onchange = laden;

  const vorlageAnwenden = v => {
    zuordnung = {};
    kopfzeile = v.kopfzeile;
    datei.ueberschriften.forEach((u, i) => { if (v.ueberschriften?.[u]) zuordnung[i] = v.ueberschriften[u]; });
    if (!Object.keys(zuordnung).length) Object.entries(v.spalten ?? {}).forEach(([i, f]) => { if (Number(i) < datei.spalten) zuordnung[i] = f; });
    zuordnen();
  };

  function zuordnen() {
    const box = ziel.querySelector('[data-zuordnung]');
    const zeilen = datei.vorschau;
    box.innerHTML = `<div class="card mb-3"><div class="card-body pb-2">
        <div class="si-schritt"><b>2</b>Spalten zuordnen</div>
        <div class="d-flex flex-wrap gap-2 align-items-center mb-2">
          <div class="form-check form-switch me-2"><input class="form-check-input" type="checkbox" id="si-kopf" ${kopfzeile ? 'checked' : ''} data-kopf><label class="form-check-label" for="si-kopf">Erste Zeile ist Kopfzeile</label></div>
          <select class="form-select form-select-sm" style="max-width:16rem" data-vorlage aria-label="Vorlage anwenden"><option value="">Vorlage anwenden …</option>${g.vorlagen.map((v, i) => `<option value="${i}">${e(v.name)}</option>`).join('')}</select>
          <button class="btn btn-sm btn-outline-secondary" data-vorlage-speichern>${icon('save')}Als Vorlage speichern</button>
          ${g.vorlagen.length ? `<button class="btn btn-sm btn-outline-danger" data-vorlage-loeschen title="Gewählte Vorlage löschen">${icon('loeschen')}</button>` : ''}
          <span class="small text-body-secondary ms-auto">Vorschau der ersten Zeilen</span></div></div>
      <div class="table-responsive"><table class="table table-sm table-card align-middle mb-0 si-tabelle"><thead><tr>
        ${datei.ueberschriften.map((u, i) => `<th><div>${e(kopfzeile ? u : `Spalte ${i + 1}`)}</div><select class="form-select form-select-sm" data-spalte="${i}">${felderOptionen(zuordnung[i])}</select></th>`).join('')}</tr></thead><tbody>
        ${zeilen.map((z, n) => `<tr class="${n === 0 && kopfzeile ? 'kopf' : ''}">${datei.ueberschriften.map((_, i) => `<td title="${e(z[i] ?? '')}">${e(z[i] ?? '')}</td>`).join('')}</tr>`).join('')}</tbody></table></div>
      <div class="card-body border-top">
        <div class="si-schritt"><b>3</b>Abgleich</div>
        <div class="row g-3">
          <div class="col-md-4"><label class="form-label">Modus</label><select class="form-select" data-opt="modus"><option value="aktualisieren">Neue anlegen und vorhandene aktualisieren</option><option value="neu">Nur neue anlegen</option></select></div>
          <div class="col-md-4"><label class="form-label">Vorhandene erkennen über</label><select class="form-select" data-opt="schluessel"><option value="auto">ID/externe Nummer, sonst Name + Geburtsdatum</option><option value="id">nur ID bzw. externe Nummer</option><option value="name_geburt">Name + Geburtsdatum</option></select></div>
          <div class="col-md-4"><label class="form-label">Login-Schema für Konten</label><select class="form-select" data-opt="schema">${g.schemata.filter(s => s !== 'kuerzel').map(s => `<option>${s}</option>`).join('')}</select></div>
        </div>
        <div class="d-flex flex-wrap gap-3 mt-3">
          <div class="form-check"><input class="form-check-input" type="checkbox" id="si-aus" data-opt="austrittMarkieren"><label class="form-check-label" for="si-aus">Nicht enthaltene Schüler*innen als ausgetreten markieren</label></div>
          ${g.darfKonten ? `<div class="form-check"><input class="form-check-input" type="checkbox" id="si-ks" data-opt="kontenSchueler"><label class="form-check-label" for="si-ks">Konten für Schüler*innen anlegen</label></div>
          <div class="form-check"><input class="form-check-input" type="checkbox" id="si-ke" data-opt="kontenEltern"><label class="form-check-label" for="si-ke">Konten für Erziehungsberechtigte anlegen</label></div>` : ''}
        </div>
        ${g.quelle === 'export' ? alarm('warning', 'Die Schülerdaten kommen aus dem UntPlan-Export. Importierte Änderungen ergänzen den Export; für eine vollständige Pflege in UntPlanWeb oben auf „Web-Verwaltung“ umstellen.') : ''}
        <div class="d-flex gap-2 mt-3"><button class="btn btn-outline-primary" data-pruefen>${icon('suchen')}Trockenlauf</button></div>
      </div></div>`;
    box.querySelector('[data-kopf]').onchange = ev => { kopfzeile = ev.target.checked; zuordnen(); };
    box.querySelectorAll('[data-spalte]').forEach(s => s.onchange = () => {
      // ein Feld nur einmal zuordnen
      if (s.value) Object.keys(zuordnung).forEach(k => { if (zuordnung[k] === s.value) delete zuordnung[k]; });
      if (s.value) zuordnung[s.dataset.spalte] = s.value; else delete zuordnung[s.dataset.spalte];
      zuordnen();
    });
    box.querySelector('[data-vorlage]').onchange = ev => { if (ev.target.value !== '') vorlageAnwenden(g.vorlagen[Number(ev.target.value)]); };
    box.querySelector('[data-vorlage-loeschen]')?.addEventListener('click', async () => {
      const i = box.querySelector('[data-vorlage]').value;
      if (i === '') { toast('Bitte zuerst eine Vorlage wählen.', true); return; }
      const v = g.vorlagen[Number(i)];
      if (!await bestaetigen('Vorlage löschen', `Vorlage „${v.name}“ löschen?`)) return;
      await api(`/api/app/admin/schuelerimport/vorlagen/${encodeURIComponent(v.name)}`, { methode: 'DELETE' });
      g.vorlagen.splice(Number(i), 1);
      zuordnen();
    });
    box.querySelector('[data-vorlage-speichern]').onclick = () => dialog({
      titel: 'Zuordnung als Vorlage speichern',
      inhalt: '<label class="form-label">Name der Vorlage</label><input class="form-control" name="name" maxlength="100" placeholder="z. B. Export Schulverwaltung">',
      knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async b => {
        const name = b.querySelector('[name=name]').value;
        const ueberschriften = {};
        if (kopfzeile) Object.entries(zuordnung).forEach(([i, f]) => { ueberschriften[datei.ueberschriften[i]] = f; });
        const v = await senden('/api/app/admin/schuelerimport/vorlagen', { name, kopfzeile, ueberschriften, spalten: zuordnung });
        g.vorlagen = g.vorlagen.filter(x => x.name.toLowerCase() !== v.name.toLowerCase()).concat(v).sort((a, c) => a.name.localeCompare(c.name));
        toast('Vorlage gespeichert.'); zuordnen();
      } }],
    });
    box.querySelector('[data-pruefen]').onclick = () => ausfuehren(false);
  }

  const optionen = () => {
    const o = {};
    ziel.querySelectorAll('[data-opt]').forEach(f => { o[f.dataset.opt] = f.type === 'checkbox' ? f.checked : f.value; });
    return o;
  };
  const anfrage = () => ({ zeilen: datei.zeilen, kopfzeile, zuordnung, optionen: optionen() });

  async function ausfuehren(uebernehmen) {
    const box = ziel.querySelector('[data-ergebnis]');
    try {
      const r = await senden(`/api/app/admin/schuelerimport/${uebernehmen ? 'uebernehmen' : 'pruefen'}`, anfrage());
      const chips = Object.entries(STATUS).filter(([k]) => r[k]).map(([k, [farbe, t]]) => `<span class="u-chip ${farbe}">${r[k]} ${t}</span>`).join('');
      box.innerHTML = `<div class="card mb-3"><div class="card-header d-flex flex-wrap gap-2 align-items-center">
          <span class="si-schritt mb-0"><b>4</b>${uebernehmen ? 'Ergebnis der Übernahme' : 'Trockenlauf'}</span>${chips}
          ${uebernehmen ? '' : `<button class="btn btn-primary btn-sm ms-auto" data-uebernehmen ${r.neu + r.geaendert + r.ausgetreten ? '' : 'disabled'}>Übernehmen</button>`}</div>
        ${uebernehmen ? '' : `<div class="card-body py-2 small text-body-secondary">Es wurde noch nichts gespeichert.${r.fehler ? ' Zeilen mit Fehlern werden beim Übernehmen übersprungen.' : ''}</div>`}
        <div class="table-responsive" style="max-height:480px"><table class="table table-sm table-card align-middle mb-0"><thead><tr><th>Zeile</th><th>Status</th><th>Name</th><th>Klasse</th><th>Details</th></tr></thead><tbody>
        ${r.zeilen.slice(0, 2000).map(z => `<tr class="${z.status === 'fehler' ? 'si-zeile-fehler' : ''}"><td>${z.zeile || ''}</td><td><span class="u-chip ${STATUS[z.status]?.[0] ?? ''}">${STATUS[z.status]?.[1] ?? e(z.status)}</span></td>
          <td class="fw-semibold">${e(z.name)}</td><td>${e(z.klasse)}</td>
          <td>${z.fehler ? `<span class="text-danger">${e(z.fehler)}</span>` : ''}${[...z.aenderungen, ...z.hinweise].map(a => `<div class="si-aenderung">${e(a)}</div>`).join('')}</td></tr>`).join('')}
        </tbody></table></div></div>`;
      box.querySelector('[data-uebernehmen]')?.addEventListener('click', async () => {
        const o = optionen();
        const text = `${r.neu} neu, ${r.geaendert} geändert${r.ausgetreten ? `, ${r.ausgetreten} als ausgetreten markieren` : ''}${o.kontenSchueler || o.kontenEltern ? ', Konten anlegen' : ''}. Fortfahren?`;
        if (!await bestaetigen('Schüler*innen übernehmen', text, 'Übernehmen')) return;
        ausfuehren(true);
      });
      if (uebernehmen) {
        toast('Import übernommen.');
        if (r.konten?.length) dialog({ titel: 'Neue Konten', breit: true, inhalt: `<p>${r.konten.length} Konten angelegt.</p>${alarm('warning', 'Die Startpasswörter werden nur jetzt angezeigt. Bitte die Liste herunterladen.')}`,
          knoepfe: [{ text: 'Liste herunterladen (CSV)', aktion: () => { herunterladen(`konten-${heuteIso()}.csv`, r.csv); return false; } }, { text: 'Schließen', art: 'haupt' }] });
      }
      box.scrollIntoView({ behavior: 'smooth', block: 'start' });
    } catch (err) { toast(err.message, true); }
  }
}
