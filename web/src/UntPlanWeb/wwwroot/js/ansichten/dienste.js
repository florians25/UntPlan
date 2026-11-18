// Klassenbuch → Klassendienste wie im Original: Klasse, Dienst und Zeitraum wählen; Zeitansicht (Schüler*innen × Wochen zum
// Ankreuzen) oder Listenansicht; automatische Schülereinteilung reihum; Bericht.
import { e, icon, holen, senden, api, toast, heuteIso, plusTage, montag, leer, suchfeldMitFilter, suchfeldMitFilterBinden, zeitraumPillen, zeilenFiltern } from '../ui.js';
import { kbDialog as dialog, kbBestaetigen as bestaetigen } from '../kbdialog.js';
import { zeitbereichFeld, zeitbereichBinden } from '../zeitbereich.js';

const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';
const kurz = iso => { const [, m, d] = iso.split('-'); return `${Number(d)}.${Number(m)}.`; };

export async function zeigen(seite, kontext, [klasseParam, dienstParam, ansichtParam, vonParam, bisParam]) {
  const ich = kontext.ich;
  const el = await kontext.elemente();
  const eigene = el.klassen.find(k => ich.element && (k.leitung ?? []).some(l => l.toLowerCase() === ich.element.toLowerCase()))?.kurz;
  const liste = ansichtParam !== 'zeit';
  const alleKlassen = klasseParam === 'alle' && liste;
  const klasse = alleKlassen ? '' : klasseParam && klasseParam !== '-' && klasseParam !== 'alle' ? klasseParam : eigene ?? el.klassen[0]?.kurz ?? '';
  const iso = /^\d{4}-\d{2}-\d{2}$/;
  const sj = kontext.sitzung.schuljahr;
  // Listenansicht: Datumsbereich automatisch das ganze Schuljahr (Leitfaden Klassenleitung, Klassensprecher eintragen)
  const von = iso.test(vonParam ?? '') ? vonParam : liste && sj?.von ? sj.von : sj?.bis ? heuteIso() : montag(heuteIso());
  const bis = iso.test(bisParam ?? '') ? bisParam : sj?.bis ? sj.bis : (sj?.bis && sj.bis > von ? (plusTage(von, 7 * 10) < sj.bis ? plusTage(von, 7 * 10 - 1) : sj.bis) : plusTage(von, 69));
  const d = await holen(`/api/app/klassendienste?klasse=${encodeURIComponent(klasse)}&von=${von}&bis=${bis}`);
  const alleDienste = liste && (!dienstParam || dienstParam === '-' || dienstParam === 'alle');
  const dienst = d.arten.find(a => String(a.id) === dienstParam) ?? d.arten.find(a => a.name === 'Klassenordner') ?? d.arten[0];
  const ziel = (k = alleKlassen ? 'alle' : klasse, di = alleDienste ? 'alle' : dienst?.id, a = liste ? 'liste' : 'zeit', v = von, b = bis) => `#/dienste/${encodeURIComponent(k || '-')}/${di ?? '-'}/${a}/${v}/${b}`;
  const zuordnungen = d.zuordnungen.filter(z => alleDienste || !dienst || z.dienstId === dienst.id);
  kontext.titel(liste ? '' : klasse && dienst ? `Klassendienste: ${klasse} | ${dienst.name}` : 'Klassendienste',
    liste ? '' : `<a class="kl-link" href="${ziel(klasse, dienst?.id, 'liste')}">Zur Listenansicht wechseln</a>`);
  if (liste) return altListe();

  seite.innerHTML = `
    <div class="u-sf-leiste u-kopfteil">
      ${suchfeldMitFilter({ platzhalter: 'Name suchen', label: 'Schüler*in suchen', filter: [
        { name: 'klasse', label: 'Klasse', alle: null, standard: klasse, wert: klasse, optionen: el.klassen.map(k => [k.kurz, k.kurz]) },
        { name: 'dienst', label: 'Dienst', alle: null, standard: String(dienst?.id ?? ''), wert: String(dienst?.id ?? ''), optionen: d.arten.length ? d.arten.map(a => [String(a.id), a.name]) : [['', 'Keine Dienste angelegt']] },
      ] })}
      ${zeitbereichFeld(von, bis, sj)}
      <div class="u-sf-rechts">
        ${d.verwalten && !liste ? '<button class="btn btn-outline-secondary" data-auto>Automatische Schülereinteilung</button>' : ''}
        ${d.verwalten && liste ? `<button class="btn btn-primary" data-neu>${icon('plus')}Neu</button>` : ''}
        <a class="btn btn-outline-secondary" href="#/berichte/klassendienste/${encodeURIComponent(klasse)}/${von}/${bis}">Bericht</a>
      </div>
    </div>
    <div data-inhalt></div>`;
  const f = n => seite.querySelector(`[data-${n}]`)?.value ?? '';
  const sf = suchfeldMitFilterBinden(seite, {
    beiSuche: t => zeilenFiltern(seite.querySelectorAll('.kd-raster tbody tr'), t),
    beiFilter: () => zeitNeu(),
  });
  const zeitNeu = () => kontext.navigieren(ziel(sf.wert('klasse'), sf.wert('dienst'), liste ? 'liste' : 'zeit', f('von') || von, f('bis') || bis));
  zeitbereichBinden(seite, sj, zeitNeu);
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

  // Klassendienste als Liste im nl-design: Filterleiste (Klasse, Dienst als Pillen, Zeitraum), Tabelle Dienst | Schüler*innen | Zeitraum
  // (gleicher Dienst und Zeitraum in einer Zeile), Anlegen und Bearbeiten in einem Dialog mit Mehrfachauswahl der Schüler*innen.
  function altListe() {
    // Listenansicht wie im Original: Klasse | Art | Zeitraum (Datumsbereich automatisch das ganze Schuljahr), Tabelle Auswahl | Stift/X | Art |
    // Dienst | Name | Klasse | Zeit | Text, darunter „… Elemente gefunden, Anzeige aller Elemente.“ und Neu | Löschen | Berichte.
    const darf = d.verwalten || ich.admin;
    const zeilen = [...zuordnungen].sort((a, b) => a.von.localeCompare(b.von) || a.dienst.localeCompare(b.dienst, 'de') || a.schueler.localeCompare(b.schueler, 'de'));
    const gruppeVon = z => ({ dienstId: z.dienstId, dienst: z.dienst, von: z.von, bis: z.bis, klasse: z.klasse, text: z.text, eintraege: [z] });
    kontext.titel('Klassendienste', `<a class="kl-link" href="${ziel(klasse || el.klassen[0]?.kurz, dienst?.id, 'zeit')}">Zur Zeitansicht wechseln</a>`);
    seite.innerHTML = `<div class="kd-seite">
      <div class="nl-leiste sg-leiste u-kopfteil kd-leiste">
        <label class="sg-feld"><span>Klasse</span><select class="form-select" data-klasse><option value="alle">- Alle -</option>${el.klassen.map(k => `<option ${k.kurz === klasse ? 'selected' : ''}>${e(k.kurz)}</option>`).join('')}</select></label>
        <label class="sg-feld"><span>Art</span><select class="form-select" data-art><option value="alle">- Alle -</option>${d.arten.map(a => `<option value="${a.id}" ${!alleDienste && a.id === dienst?.id ? 'selected' : ''}>${e(a.name)}</option>`).join('')}</select></label>
        ${zeitbereichFeld(von, bis, sj)}
        <div class="u-fl-aktionen kl-knoepfe">
          ${d.verwalten ? `<button type="button" class="nl-knopf" data-neu>Neu</button>` : ''}
          ${darf ? '<button type="button" class="nl-knopf" data-sammel-loeschen disabled>Löschen</button>' : ''}
          <details class="kl-menue"><summary class="nl-knopf">Berichte <i class="bi bi-caret-down-fill"></i></summary>
            <div class="kl-menue-liste"><a href="#/berichte/klassendienste/${encodeURIComponent(klasse || '-')}/${von}/${bis}">Klassendienste</a></div></details>
        </div>
      </div>
      <div class="nl-tabelle kd-liste-tabelle">${zeilen.length ? `<table><thead><tr>${darf ? '<th class="nl-wahl"><span class="sg-kopfwahl">Auswahl<input type="checkbox" class="nl-rund" data-alle aria-label="Alle auswählen"></span></th><th></th>' : ''}
          <th>Art</th><th>Dienst</th><th>Name</th><th>Klasse</th><th>Zeit</th><th>Text</th></tr></thead><tbody>
        ${zeilen.map((z, i) => `<tr data-text="${e(`${z.dienst} ${z.schueler} ${z.klasse} ${z.text}`.toLowerCase())}">
          ${darf ? `<td class="nl-wahl"><input type="checkbox" class="nl-rund" data-gruppe="${i}" aria-label="${e(z.schueler)} auswählen"></td>
          <td class="text-nowrap"><button type="button" class="nl-icon sichtbar" data-bearbeiten="${i}" title="Bearbeiten" aria-label="Bearbeiten">${icon('stift')}</button><button type="button" class="nl-icon sichtbar rot" data-weg="${i}" title="Löschen" aria-label="Löschen"><i class="bi bi-x-lg"></i></button></td>` : ''}
          <td title="${e(z.dienst)}">${icon('klassendienste')}</td><td>${e(z.dienstKurz || z.dienst)}</td><td>${e(z.schueler)}</td><td>${e(z.klasse)}</td>
          <td class="text-nowrap">${z.von === z.bis ? kurz(z.von) : `${kurz(z.von)}-${kurz(z.bis)}`}</td><td>${e(z.text)}</td></tr>`).join('')}
        </tbody></table>` : leer('Nichts anzuzeigen')}
        ${zeilen.length ? `<div class="sg-fuss"><span>${zeilen.length === 1 ? 'Ein Element gefunden.' : `${zeilen.length} Elemente gefunden, Anzeige aller Elemente.`}</span></div>` : ''}
      </div>
      ${!d.verwalten && klasse ? '<p class="nl-hinweis mt-3"><i class="bi bi-info-circle"></i>Einteilen kann die Klassenleitung bzw. die Schulleitung.</p>' : ''}
    </div>`;
    const f = n => seite.querySelector(`[data-${n}]`)?.value ?? '';
    const hin = (k, a, v = f('von') || von, b = f('bis') || bis) => kontext.navigieren(`#/dienste/${encodeURIComponent(k)}/${a}/liste/${v}/${b}`);
    seite.querySelector('[data-klasse]').onchange = () => hin(f('klasse') || 'alle', f('art') || 'alle');
    seite.querySelector('[data-art]').onchange = () => hin(f('klasse') || 'alle', f('art') || 'alle');
    zeitbereichBinden(seite, sj, (v, b) => hin(f('klasse') || 'alle', f('art') || 'alle', v, b));
    const knopf = seite.querySelector('[data-sammel-loeschen]');
    const aktualisieren = () => { if (knopf) knopf.disabled = !seite.querySelectorAll('[data-gruppe]:checked').length; };
    seite.querySelectorAll('[data-gruppe]').forEach(c => c.onchange = aktualisieren);
    seite.querySelector('[data-alle]')?.addEventListener('change', ev => { seite.querySelectorAll('[data-gruppe]').forEach(c => { c.checked = ev.target.checked; }); aktualisieren(); });
    const neu = () => kontext.navigieren(location.hash);
    seite.querySelector('[data-neu]')?.addEventListener('click', () => einteilenDialog());
    seite.querySelectorAll('[data-bearbeiten]').forEach(b => b.onclick = () => einteilenDialog(gruppeVon(zeilen[Number(b.dataset.bearbeiten)])));
    const loeschen = async ids => {
      if (!ids.length) return;
      if (!await bestaetigen('Klassendienste', `${ids.length === 1 ? 'Diesen Eintrag' : `${ids.length} Einträge`} löschen?`)) return;
      try { for (const id of ids) await api(`/api/app/klassendienste/${id}`, { methode: 'DELETE' }); toast('Gelöscht.'); } catch (err) { kontext.fehler(err); }
      neu();
    };
    seite.querySelectorAll('[data-weg]').forEach(b => b.onclick = () => loeschen([zeilen[Number(b.dataset.weg)].id]));
    knopf?.addEventListener('click', () => loeschen([...seite.querySelectorAll('[data-gruppe]:checked')].map(c => zeilen[Number(c.dataset.gruppe)].id)));
    /** Ein Dialog für Anlegen und Bearbeiten: Dienst, Zeitraum, Schüler*innen (Mehrfachauswahl mit Suche), Text. */
    async function einteilenDialog(g = null) {
      let schueler = d.schueler;
      const kl = g?.klasse || klasse;
      if (!schueler.length && kl) schueler = await holen(`/api/app/klassenbuch/schueler?klasse=${encodeURIComponent(kl)}`).catch(() => []);
      const gewaehlt = new Set((g?.eintraege ?? []).map(z => z.schuelerId));
      // neuer Dienst: Datumsbereich der Liste (Standard: ganzes Schuljahr, z. B. Klassensprecher*in)
      const vonV = g?.von ?? von, bisV = g?.bis ?? bis;
      dialog({
        titel: g ? `Klassendienste ${g.klasse || kl}` : `Klassendienste ${kl}`,
        breit: true,
        inhalt: `<div class="kd-dialog">
          <label class="kd-dfeld"><span>Dienst</span><select class="form-select" name="dienstId">${g ? '' : '<option value="">- Auswahl -</option>'}${d.arten.map(a => `<option value="${a.id}" ${g && a.id === g.dienstId ? 'selected' : ''}>${e(a.name)}</option>`).join('')}</select></label>
          <div class="kd-dialog-zwei">
            <div class="kd-auswahl"><input type="search" class="form-control" data-suche placeholder="Name suchen …" aria-label="Schüler*in suchen">
              <div class="kd-liste">${schueler.map(x => `<label class="kd-option" data-name="${e(`${x.name} ${x.vorname}`.toLowerCase())}"><input type="checkbox" value="${x.id}" ${gewaehlt.has(x.id) ? 'checked' : ''}>${e(x.name)} ${e(x.vorname)}</label>`).join('') || '<div class="small text-body-secondary p-2">Keine Schülerliste übertragen.</div>'}</div></div>
            <div><label class="kd-dfeld"><span>Von</span><input type="date" class="form-control" name="von" value="${vonV}"></label>
              <label class="kd-dfeld"><span>Bis</span><input type="date" class="form-control" name="bis" value="${bisV}"></label></div>
          </div>
          <label class="kd-dfeld"><span>Anmerkung</span><input class="form-control" name="text" maxlength="200" value="${e(g?.text ?? '')}"></label>
        </div>`,
        beimOeffnen: box => {
          box.querySelector('[data-suche]').oninput = ev => { const q = ev.target.value.trim().toLowerCase(); box.querySelectorAll('.kd-option').forEach(o => { o.hidden = q && !o.dataset.name.includes(q); }); };
        },
        knoepfe: [
          { text: 'Speichern', art: 'haupt', aktion: async box => {
            const q = n => box.querySelector(`[name=${n}]`).value;
            if (!q('dienstId')) { toast('Bitte einen Dienst auswählen (z. B. „Klassensprecher*in“ oder „Klassensprecher*in Stv.“).', true); return false; }
            const ids = [...box.querySelectorAll('.kd-liste input:checked')].map(c => Number(c.value));
            if (!ids.length) { toast('Bitte mindestens eine Schülerin bzw. einen Schüler wählen.', true); return false; }
            if (q('bis') < q('von')) { toast('Das Ende liegt vor dem Beginn.', true); return false; }
            const daten = sid => ({ dienstId: Number(q('dienstId')), schuelerId: sid, von: q('von'), bis: q('bis'), text: q('text') });
            for (const z of g?.eintraege ?? []) {
              if (ids.includes(z.schuelerId)) await senden(`/api/app/klassendienste/${z.id}`, daten(z.schuelerId), 'PUT');
              else await api(`/api/app/klassendienste/${z.id}`, { methode: 'DELETE' });
            }
            for (const sid of ids.filter(x => !gewaehlt.has(x))) await senden('/api/app/klassendienste', daten(sid));
            toast('Gespeichert.'); neu();
          } },
          ...(g ? [{ text: 'Löschen', art: 'gefahr', aktion: async () => {
            for (const z of g.eintraege) await api(`/api/app/klassendienste/${z.id}`, { methode: 'DELETE' });
            toast('Einteilung gelöscht.'); neu();
          } }] : []),
          { text: 'Schließen' },
        ],
      });
    }
  }

  function formular(z = {}) { return formularHtml(z); }
  function werte(box) { return werteAus(box); }
  function formularHtml(z = {}) { return `<div class="row g-3">
      <div class="col-sm-6"><label class="form-label">Dienst</label><select class="form-select" name="dienstId">${d.arten.map(a => `<option value="${a.id}" ${a.id === (z.dienstId ?? dienst.id) ? 'selected' : ''}>${e(a.name)}</option>`).join('')}</select></div>
      <div class="col-sm-6"><label class="form-label">Schüler*in</label><select class="form-select" name="schuelerId">${d.schueler.map(s => `<option value="${s.id}" ${s.id === z.schuelerId ? 'selected' : ''}>${e(s.name)} ${e(s.vorname)}</option>`).join('')}</select></div>
      <div class="col-sm-6"><label class="form-label">Von</label><input type="date" class="form-control" name="von" value="${z.von ?? montag(heuteIso())}"></div>
      <div class="col-sm-6"><label class="form-label">Bis</label><input type="date" class="form-control" name="bis" value="${z.bis ?? plusTage(montag(heuteIso()), 4)}"></div>
      <div class="col-12"><label class="form-label">Text</label><input class="form-control" name="text" value="${e(z.text ?? '')}"></div></div>`; }
  function werteAus(box) { return { dienstId: Number(box.querySelector('[name=dienstId]').value), schuelerId: Number(box.querySelector('[name=schuelerId]').value),
    von: box.querySelector('[name=von]').value, bis: box.querySelector('[name=bis]').value, text: box.querySelector('[name=text]').value }; }
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
