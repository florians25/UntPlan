// Klassenbuch → Klassendienste wie im Original: Klasse, Dienst und Zeitraum wählen; Zeitansicht (Schüler*innen × Wochen zum
// Ankreuzen) oder Listenansicht; automatische Schülereinteilung reihum; Bericht.
import { e, icon, holen, senden, api, toast, dialog, bestaetigen, filterFeld, heuteIso, plusTage, montag, leer } from '../ui.js';

const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';
const kurz = iso => { const [, m, d] = iso.split('-'); return `${Number(d)}.${Number(m)}.`; };

export async function zeigen(seite, kontext, [klasseParam, dienstParam, ansichtParam, vonParam, bisParam]) {
  const ich = kontext.ich;
  const el = await kontext.elemente();
  const eigene = el.klassen.find(k => ich.element && (k.leitung ?? []).some(l => l.toLowerCase() === ich.element.toLowerCase()))?.kurz;
  const klasse = klasseParam && klasseParam !== '-' ? klasseParam : eigene ?? el.klassen[0]?.kurz ?? '';
  const iso = /^\d{4}-\d{2}-\d{2}$/;
  const sj = kontext.sitzung.schuljahr;
  const von = iso.test(vonParam ?? '') ? vonParam : montag(heuteIso());
  const bis = iso.test(bisParam ?? '') ? bisParam : (sj?.bis && sj.bis > von ? (plusTage(von, 7 * 10) < sj.bis ? plusTage(von, 7 * 10 - 1) : sj.bis) : plusTage(von, 69));
  const liste = ansichtParam === 'liste';
  const d = await holen(`/api/app/klassendienste?klasse=${encodeURIComponent(klasse)}&von=${von}&bis=${bis}`);
  const dienst = d.arten.find(a => String(a.id) === dienstParam) ?? d.arten.find(a => a.name === 'Klassenordner') ?? d.arten[0];
  const ziel = (k = klasse, di = dienst?.id, a = liste ? 'liste' : 'zeit', v = von, b = bis) => `#/dienste/${encodeURIComponent(k || '-')}/${di ?? '-'}/${a}/${v}/${b}`;
  const zuordnungen = d.zuordnungen.filter(z => !dienst || z.dienstId === dienst.id);
  kontext.titel(klasse && dienst ? `Klassendienste: ${klasse} | ${dienst.name}` : 'Klassendienste',
    `<a class="btn btn-outline-secondary" href="${ziel(klasse, dienst?.id, liste ? 'zeit' : 'liste')}">${liste ? 'Zur Zeitansicht wechseln' : 'Zur Listenansicht wechseln'}</a>`);

  seite.innerHTML = `
    <div class="u-filter">
      ${filterFeld('Klasse', `<select class="form-select" data-klasse>${el.klassen.map(k => `<option ${k.kurz === klasse ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select>`)}
      ${filterFeld('Dienst', `<select class="form-select" data-dienst>${d.arten.map(a => `<option value="${a.id}" ${a.id === dienst?.id ? 'selected' : ''}>${e(a.name)}</option>`).join('') || '<option>Keine Dienste angelegt</option>'}</select>`)}
      ${filterFeld('Von', `<input type="date" class="form-control" data-von value="${von}">`)}
      ${filterFeld('Bis', `<input type="date" class="form-control" data-bis value="${bis}">`)}
      <div class="u-rechts">
        ${d.verwalten && !liste ? '<button class="btn btn-outline-secondary" data-auto>Automatische Schülereinteilung</button>' : ''}
        ${d.verwalten && liste ? `<button class="btn btn-primary" data-neu>${icon('plus')}Neu</button>` : ''}
        <a class="btn btn-outline-secondary" href="#/berichte/klassendienste/${encodeURIComponent(klasse)}/${von}/${bis}">Bericht</a>
      </div>
    </div>
    <div data-inhalt></div>`;
  const f = n => seite.querySelector(`[data-${n}]`)?.value ?? '';
  seite.querySelectorAll('.u-filter select, .u-filter input').forEach(x => x.onchange = () => kontext.navigieren(ziel(f('klasse'), f('dienst'), liste ? 'liste' : 'zeit', f('von') || von, f('bis') || bis)));
  const inhalt = seite.querySelector('[data-inhalt]');
  const neu = () => kontext.navigieren(location.hash);
  if (!klasse) { inhalt.innerHTML = leer('Keine Klassen vorhanden.'); return; }
  if (!dienst) { inhalt.innerHTML = leer('Es sind noch keine Dienste angelegt (Stammdaten → Dienste).'); return; }

  if (liste) {
    inhalt.innerHTML = `<div class="u-tabelle-karte">${zuordnungen.length ? `<table class="u-tabelle"><thead><tr><th></th><th>Dienst</th><th>Schüler*in</th><th>Klasse</th><th>Zeit</th><th>Text</th><th></th></tr></thead><tbody>
      ${zuordnungen.map(z => `<tr><td>${icon('klassendienste')}</td><td>${e(z.dienstKurz || z.dienst)}</td><td class="fw-semibold">${e(z.schueler)}</td><td>${e(z.klasse)}</td>
        <td class="text-nowrap">${z.von === z.bis ? kurz(z.von) : `${kurz(z.von)} - ${kurz(z.bis)}`}</td><td>${e(z.text)}</td>
        <td class="text-end text-nowrap">${d.verwalten ? `<button class="btn btn-sm btn-outline-secondary" data-bearbeiten="${z.id}" title="Bearbeiten">${icon('stift')}</button>
          <button class="btn btn-sm btn-outline-danger" data-loeschen="${z.id}" title="Löschen">${icon('loeschen')}</button>` : ''}</td></tr>`).join('')}</tbody></table>`
      : leer('Im Zeitraum ist niemand eingeteilt.', 'people')}</div>`;
  } else {
    const wochen = [];
    for (let w = montag(von); w <= bis && wochen.length < 60; w = plusTage(w, 7)) wochen.push(w);
    const aktiv = (sid, w) => zuordnungen.some(z => z.schuelerId === sid && z.von <= plusTage(w, 6) && z.bis >= w);
    inhalt.innerHTML = d.schueler.length ? `<div class="u-tabelle-karte kd-raster p-0"><table><thead><tr><th>Schüler*in</th>${wochen.map(w => `<th title="Woche ab ${datumDe(w)}">${kurz(w)}</th>`).join('')}<th class="summe">Σ</th></tr></thead><tbody>
      ${d.schueler.map(s => { const n = wochen.filter(w => aktiv(s.id, w)).length; return `<tr><td>${e(s.name)} ${e(s.vorname)}</td>
        ${wochen.map(w => `<td><input type="checkbox" class="form-check-input" data-woche="${w}" data-schueler="${s.id}" ${aktiv(s.id, w) ? 'checked' : ''} ${d.verwalten ? '' : 'disabled'} aria-label="${e(s.vorname)} ${e(s.name)} Woche ${datumDe(w)}"></td>`).join('')}
        <td class="summe">${n || ''}</td></tr>`; }).join('')}</tbody>
      <tfoot><tr><td>Summe</td>${wochen.map(w => `<td>${d.schueler.filter(s => aktiv(s.id, w)).length || ''}</td>`).join('')}<td></td></tr></tfoot></table></div>
      ${d.verwalten ? '' : '<p class="small text-body-secondary mt-2">Einteilen kann die Klassenleitung bzw. die Schulleitung.</p>'}`
      : leer('Für diese Klasse wurde keine Schülerliste aus UntPlan übertragen.', 'people');
    inhalt.querySelectorAll('[data-woche]').forEach(c => c.onchange = async () => {
      try { await senden('/api/app/klassendienste/woche', { dienstId: dienst.id, schuelerId: Number(c.dataset.schueler), datum: c.dataset.woche, an: c.checked }); neu(); }
      catch (err) { c.checked = !c.checked; kontext.fehler(err); }
    });
  }

  const formular = (z = {}) => `<div class="row g-3">
      <div class="col-sm-6"><label class="form-label">Dienst</label><select class="form-select" name="dienstId">${d.arten.map(a => `<option value="${a.id}" ${a.id === (z.dienstId ?? dienst.id) ? 'selected' : ''}>${e(a.name)}</option>`).join('')}</select></div>
      <div class="col-sm-6"><label class="form-label">Schüler*in</label><select class="form-select" name="schuelerId">${d.schueler.map(s => `<option value="${s.id}" ${s.id === z.schuelerId ? 'selected' : ''}>${e(s.name)} ${e(s.vorname)}</option>`).join('')}</select></div>
      <div class="col-sm-6"><label class="form-label">Von</label><input type="date" class="form-control" name="von" value="${z.von ?? montag(heuteIso())}"></div>
      <div class="col-sm-6"><label class="form-label">Bis</label><input type="date" class="form-control" name="bis" value="${z.bis ?? plusTage(montag(heuteIso()), 4)}"></div>
      <div class="col-12"><label class="form-label">Text</label><input class="form-control" name="text" value="${e(z.text ?? '')}"></div></div>`;
  const werte = box => ({ dienstId: Number(box.querySelector('[name=dienstId]').value), schuelerId: Number(box.querySelector('[name=schuelerId]').value),
    von: box.querySelector('[name=von]').value, bis: box.querySelector('[name=bis]').value, text: box.querySelector('[name=text]').value });
  seite.querySelector('[data-neu]')?.addEventListener('click', () => dialog({ titel: 'Klassendienst eintragen', inhalt: formular(), knoepfe: [{ text: 'Abbrechen' },
    { text: 'Speichern', art: 'haupt', aktion: async box => { await senden('/api/app/klassendienste', werte(box)); toast('Gespeichert.'); neu(); } }] }));
  inhalt.querySelectorAll('[data-bearbeiten]').forEach(b => b.onclick = () => {
    const z = zuordnungen.find(x => x.id === Number(b.dataset.bearbeiten));
    dialog({ titel: 'Klassendienst', inhalt: formular(z), knoepfe: [{ text: 'Abbrechen' },
      { text: 'Speichern', art: 'haupt', aktion: async box => { await senden(`/api/app/klassendienste/${z.id}`, werte(box), 'PUT'); toast('Gespeichert.'); neu(); } }] });
  });
  inhalt.querySelectorAll('[data-loeschen]').forEach(b => b.onclick = async () => {
    if (!await bestaetigen('Klassendienst löschen', 'Diese Einteilung löschen?')) return;
    await api(`/api/app/klassendienste/${b.dataset.loeschen}`, { methode: 'DELETE' });
    neu();
  });
  seite.querySelector('[data-auto]')?.addEventListener('click', () => dialog({
    titel: 'Automatische Schülereinteilung',
    inhalt: `<p class="small text-body-secondary">Teilt die Schüler*innen der Klasse ${e(klasse)} reihum (alphabetisch) für „${e(dienst.name)}“ ein. Bestehende Einteilungen ab ${datumDe(montag(von))} werden ersetzt, Ferienwochen übersprungen.</p>
      <div class="row g-3"><div class="col-sm-4"><label class="form-label">je Woche</label><input type="number" class="form-control" name="proWoche" min="1" max="10" value="2"></div>
      <div class="col-sm-4"><label class="form-label">Von</label><input type="date" class="form-control" name="von" value="${montag(von)}"></div>
      <div class="col-sm-4"><label class="form-label">Bis</label><input type="date" class="form-control" name="bis" value="${bis}"></div></div>`,
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Einteilen', art: 'haupt', aktion: async box => {
      const r = await senden('/api/app/klassendienste/automatisch', { klasse, dienstId: dienst.id, proWoche: Number(box.querySelector('[name=proWoche]').value), von: box.querySelector('[name=von]').value, bis: box.querySelector('[name=bis]').value });
      toast(`${r.eingeteilt} Einteilungen angelegt.`); neu();
    } }],
  }));
}
