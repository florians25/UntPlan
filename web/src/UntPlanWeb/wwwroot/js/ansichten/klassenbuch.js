// Klassenbuch: Wochenübersicht mit Anwesenheit, Fehlzeitenverwaltung, Vermerke, Inhalte/Hausaufgaben; Hausaufgaben für Klassenkonten.
import { e, icon, api, holen, senden, toast, dialog, bestaetigen, alarm, datumKurz, datumLang, heuteIso, plusTage, leer, lade } from '../ui.js';
import { karteDialog } from '../plangitter.js';
import { elementWahl } from '../elementwahl.js';

function verlaufTabelle(liste, kollegium) {
  if (!liste.length) return `<div class="card-body py-0">${leer('Keine Einträge im Zeitraum.')}</div>`;
  return `<div class="table-responsive"><table class="table table-sm table-hover table-card align-middle"><thead><tr><th>Datum</th><th>Std.</th><th>Fach</th>${kollegium ? '<th>Lehrkraft</th>' : ''}<th>Inhalt</th><th>Hausaufgabe</th>${kollegium ? '<th>Notiz</th>' : ''}</tr></thead><tbody>
    ${liste.map(x => `<tr><td class="text-nowrap">${e(datumKurz(x.datum))}</td><td>${x.stunde}.</td><td class="fw-semibold">${e(x.fach)}</td>${kollegium ? `<td>${e(x.lehrer)}</td>` : ''}
      <td>${e(x.thema)}</td><td>${e(x.hausaufgabe)}${x.faellig ? `<div class="small text-body-secondary">bis ${datumKurz(x.faellig)}</div>` : ''}</td>${kollegium ? `<td class="text-body-secondary small">${e(x.notiz)}</td>` : ''}</tr>`).join('')}
  </tbody></table></div>`;
}

export async function zeigen(seite, kontext, [klasseParam, reiterParam, datumParam, vonParam]) {
  const ich = kontext.ich;
  kontext.titel(ich.kollegium ? 'Klassenbuch' : 'Hausaufgaben');
  const el = await kontext.elemente();
  const klasse = ich.kollegium ? (klasseParam || el.klassen[0]?.kurz) : ich.element;

  if (!ich.kollegium) {
    if (!klasse) { seite.innerHTML = '<div class="alert alert-secondary">Ihrem Konto ist keine Klasse zugeordnet.</div>'; return; }
    const [ha, verlauf] = await Promise.all([
      holen(`/api/app/klassenbuch/hausaufgaben?klasse=${encodeURIComponent(klasse)}`),
      holen(`/api/app/klassenbuch/klasse?klasse=${encodeURIComponent(klasse)}`),
    ]);
    seite.innerHTML = `
      <div class="card mb-3"><div class="card-header">Offene Hausaufgaben · ${e(klasse)}</div>
        ${ha.length ? `<div class="table-responsive"><table class="table table-sm table-card align-middle"><thead><tr><th>Fällig</th><th>Fach</th><th>Aufgabe</th><th>Gestellt</th></tr></thead><tbody>
          ${ha.map(x => `<tr><td class="text-nowrap fw-semibold">${x.faellig ? e(datumLang(x.faellig)) : '–'}</td><td>${e(x.fach)}</td><td>${e(x.hausaufgabe)}</td><td class="text-body-secondary small">${e(datumKurz(x.datum))} · ${e(x.lehrer)}</td></tr>`).join('')}
          </tbody></table></div>` : `<div class="card-body py-0">${leer('Keine offenen Hausaufgaben.')}</div>`}</div>
      <div class="card"><div class="card-header">Unterrichtsinhalte</div>${verlaufTabelle(verlauf, false)}</div>`;
    return;
  }

  const reiter = ['woche', 'fehlzeiten', 'vermerke', 'verlauf'].includes(reiterParam) ? reiterParam : 'woche';
  const datum = /^\d{4}-\d{2}-\d{2}$/.test(datumParam ?? '') ? datumParam : heuteIso();
  const ziel = (k, r, d = datum) => `#/klassenbuch/${encodeURIComponent(k ?? '')}/${r}/${d}`;
  const REITER = { woche: ['calendar3-week', 'Wochenübersicht'], fehlzeiten: ['person-x', 'Fehlzeiten'], vermerke: ['chat-left-text', 'Vermerke'], verlauf: ['journal-text', 'Inhalte & Hausaufgaben'] };
  seite.innerHTML = `
    ${ich.element ? `<div class="card mb-3"><div class="card-header">Meine Stunden ohne Eintrag <span class="text-body-secondary fw-normal">(letzte 14 Tage)</span></div><div class="card-body" data-offen>${lade()}</div></div>` : ''}
    <div class="d-flex flex-wrap align-items-center gap-2 mb-3"><span data-wahl></span>
      <ul class="nav nav-pills ms-md-3">${Object.entries(REITER).map(([r, [ic, t]]) => `<li class="nav-item"><a class="nav-link py-1 ${r === reiter ? 'active' : ''}" href="${ziel(klasse, r)}"><i class="bi bi-${ic} me-1"></i>${t}</a></li>`).join('')}</ul>
    </div>
    <div data-reiterinhalt>${lade()}</div>`;
  if (klasse) seite.querySelector('[data-wahl]').replaceWith(elementWahl({ eintraege: el.klassen, wert: klasse, platzhalter: 'Klasse wählen', beiWahl: k => kontext.navigieren(ziel(k, reiter)) }));
  const inhalt = seite.querySelector('[data-reiterinhalt]');
  const neuLaden = () => kontext.navigieren(location.hash);
  const k2 = { ...kontext, nachSpeichern: neuLaden };

  const offenLaden = async () => {
    const z = seite.querySelector('[data-offen]');
    if (!z) return;
    const offen = await holen('/api/app/klassenbuch/offen');
    z.innerHTML = offen.length ? `<div class="d-flex flex-wrap gap-2">${offen.map((o, i) => `<button type="button" class="btn btn-sm btn-outline-secondary text-start" data-o="${i}">
        <span class="fw-semibold">${e(o.fach)}</span> ${e(o.klassen.join(', '))}<div class="small text-body-secondary">${e(datumKurz(o.datum))} · ${o.von}. Std.</div></button>`).join('')}</div>`
      : '<span class="small text-body-secondary">Alle Stunden sind eingetragen.</span>';
    z.querySelectorAll('[data-o]').forEach(b => b.onclick = () => {
      const o = offen[Number(b.dataset.o)];
      karteDialog({ datum: o.datum, von: o.von, bis: o.bis, beginn: '', ende: '', status: 'regulaer', fach: o.fach, fachName: '', klassen: o.klassen.map(k => ({ k })), lehrer: [{ k: ich.element }], raeume: [], art: '', text: '', unterricht: o.unterricht }, k2);
    });
  };
  offenLaden();
  if (!klasse) { inhalt.innerHTML = leer('Keine Klassen vorhanden.'); return; }
  const q = encodeURIComponent(klasse);

  if (reiter === 'woche') {
    const w = await holen(`/api/app/klassenbuch/woche?klasse=${q}&datum=${datum}`);
    const alleStunden = [];
    inhalt.innerHTML = `<div class="d-flex align-items-center gap-1 mb-3">
        <a class="rundknopf" href="${ziel(klasse, 'woche', plusTage(w.von, -7))}" title="Vorherige Woche">${icon('links')}</a>
        <b class="px-2">${datumKurz(w.von)} – ${datumKurz(w.bis)}${w.bis.slice(0, 4)}</b>
        <a class="rundknopf" href="${ziel(klasse, 'woche', plusTage(w.von, 7))}" title="Nächste Woche">${icon('rechts')}</a>
        <a class="heute-pill ms-2 text-decoration-none" href="${ziel(klasse, 'woche', heuteIso())}">Heute</a>
        <span class="ms-auto small text-body-secondary">${w.schuelerAnzahl ? `${w.schuelerAnzahl} Schüler/innen` : 'keine Schülerliste übertragen'}</span></div>
      ${w.tage.map(t => `<div class="card mb-3 kb-tag"><div class="card-header d-flex align-items-center gap-2">${e(datumLang(t.datum))}
          ${t.frei ? `<span class="badge text-bg-primary fw-normal"><i class="bi bi-lock-fill me-1"></i>${e(t.freiGrund || 'unterrichtsfrei')}</span>` : ''}
          ${!t.frei ? `<button type="button" class="btn btn-sm btn-link ms-auto py-0" data-vermerk="${t.datum}">${icon('plus', 'me-1')}Vermerk</button>` : ''}</div>
        ${t.frei ? '' : !t.stunden.length ? `<div class="card-body py-0">${leer('Kein Unterricht.')}</div>` : `<div class="table-responsive"><table class="table table-sm table-hover table-card align-middle kb-tabelle"><thead><tr>
          <th style="width:60px">Std.</th><th style="width:110px">Fach</th><th style="width:90px">Lehrkraft</th><th>Unterrichtsinhalt</th><th>Hausaufgabe</th><th style="width:190px">Anwesenheit</th></tr></thead><tbody>
          ${t.stunden.map(s => { alleStunden.push({ ...s, datum: t.datum }); return `<tr class="${s.status === 'entfall' ? 'text-body-secondary' : ''}" data-stunde="${alleStunden.length - 1}" role="button">
            <td class="text-nowrap">${s.von === s.bis ? s.von + '.' : s.von + '.–' + s.bis + '.'}</td>
            <td class="fw-semibold text-nowrap">${/^#[0-9a-f]{6}$/i.test(s.farbe || '') ? `<span class="fachpunkt" style="background:${s.farbe}"></span>` : ''}${s.status === 'entfall' ? `<s>${e(s.fach)}</s>` : e(s.fach)}</td>
            <td>${e(s.lehrer.join(', '))}</td>
            <td>${s.status === 'entfall' ? `<span class="badge text-bg-danger fw-normal">${e(s.art || 'Entfall')}</span>` : s.thema ? e(s.thema) : '<span class="text-body-tertiary small">– kein Eintrag –</span>'}</td>
            <td>${e(s.hausaufgabe)}${s.faellig ? `<div class="small text-body-secondary">bis ${datumKurz(s.faellig)}</div>` : ''}</td>
            <td class="small">${s.status === 'entfall' ? '' : s.fehlend.length || s.verspaetet.length
              ? `${s.fehlend.length ? `<div class="text-danger" title="${e(s.fehlend.join('; '))}"><i class="bi bi-person-x me-1"></i>${s.fehlend.length} fehlen</div>` : ''}${s.verspaetet.length ? `<div class="text-warning-emphasis" title="${e(s.verspaetet.join('; '))}"><i class="bi bi-clock me-1"></i>${s.verspaetet.length} verspätet</div>` : ''}`
              : s.geprueft ? '<span class="text-success"><i class="bi bi-check2-all me-1"></i>alle da</span>' : t.datum <= heuteIso() ? '<span class="text-body-tertiary">nicht erfasst</span>' : ''}</td></tr>`; }).join('')}
          </tbody></table></div>`}
        ${t.vermerke.length ? `<ul class="list-group list-group-flush border-top">${t.vermerke.map(v => `<li class="list-group-item small d-flex gap-2"><span class="badge ${VERMERK_FARBE[v.art] ?? 'text-bg-secondary'} fw-normal align-self-start">${e(v.art)}</span>
            <span>${v.schueler ? `<b>${e(v.schueler)}:</b> ` : ''}${e(v.text)}</span><span class="ms-auto text-body-secondary">${e(v.lehrer)}</span></li>`).join('')}</ul>` : ''}
      </div>`).join('')}`;
    inhalt.querySelectorAll('[data-stunde]').forEach(tr => tr.onclick = () => {
      const s = alleStunden[Number(tr.dataset.stunde)];
      karteDialog({ datum: s.datum, von: s.von, bis: s.bis, beginn: s.beginn, ende: s.ende, status: s.status, fach: s.fach, fachName: s.fachName, farbe: s.farbe, art: s.art, text: '',
        klassen: [{ k: klasse }], lehrer: s.lehrer.map(k => ({ k })), raeume: s.raeume.map(k => ({ k })), unterricht: s.unterricht }, k2);
    });
    inhalt.querySelectorAll('[data-vermerk]').forEach(b => b.onclick = () => vermerkDialog(klasse, b.dataset.vermerk, neuLaden));
    return;
  }

  if (reiter === 'fehlzeiten') {
    const von = /^\d{4}-\d{2}-\d{2}$/.test(vonParam ?? '') ? vonParam : plusTage(datum, -90);
    const f = await holen(`/api/app/klassenbuch/fehlzeiten?klasse=${q}&von=${von}&bis=${datum}`);
    const namen = new Map(f.schueler.map(x => [x.id, `${x.name}, ${x.vorname}`]));
    let auswahl = null;
    const zeichnen = () => {
      const eintraege = auswahl ? f.eintraege.filter(x => x.schuelerId === auswahl) : f.eintraege;
      inhalt.innerHTML = `<div class="d-flex flex-wrap align-items-center gap-2 mb-3 small">
          <span>Zeitraum</span><input type="date" class="form-control form-control-sm" style="width:auto" data-von value="${von}"><span>bis</span>
          <input type="date" class="form-control form-control-sm" style="width:auto" data-bis value="${datum}">
          ${f.verwalten ? `<button type="button" class="btn btn-sm btn-primary ms-auto" data-tage>${icon('plus', 'me-1')}Fehlen eintragen (ganze Tage)</button>` : ''}</div>
        ${!f.schueler.length ? alarm('secondary', 'Für diese Klasse wurde keine Schülerliste aus UntPlan übertragen.') : `
        <div class="row g-3"><div class="col-xl-5"><div class="card"><div class="card-header">Übersicht je Schüler/in</div>
          <div class="table-responsive"><table class="table table-sm table-hover table-card align-middle mb-0"><thead><tr><th>Name</th><th class="text-end" title="Fehlstunden">Std.</th><th class="text-end">Tage</th>
            <th class="text-end" title="entschuldigt">E</th><th class="text-end" title="unentschuldigt">U</th><th class="text-end" title="offen">offen</th><th class="text-end" title="Verspätungen">Versp.</th></tr></thead><tbody>
            ${f.schueler.map(x => `<tr role="button" data-s="${x.id}" class="${auswahl === x.id ? 'table-active' : ''}"><td>${e(x.name)}, ${e(x.vorname)}</td>
              <td class="text-end fw-semibold">${x.stunden || ''}</td><td class="text-end">${x.tage || ''}</td><td class="text-end text-success">${x.entschuldigt || ''}</td>
              <td class="text-end text-danger">${x.unentschuldigt || ''}</td><td class="text-end">${x.offen ? `<span class="badge text-bg-warning">${x.offen}</span>` : ''}</td>
              <td class="text-end">${x.verspaetungen ? `${x.verspaetungen}<small class="text-body-secondary"> (${x.minuten}′)</small>` : ''}</td></tr>`).join('')}
            </tbody></table></div></div></div>
          <div class="col-xl-7"><div class="card"><div class="card-header d-flex align-items-center gap-2">${auswahl ? e(namen.get(auswahl)) : 'Alle Einträge'}
              ${auswahl ? '<button type="button" class="btn btn-sm btn-link py-0" data-alle>alle anzeigen</button>' : ''}
              ${f.verwalten && eintraege.length ? `<div class="ms-auto btn-group btn-group-sm"><button type="button" class="btn btn-outline-success" data-status="entschuldigt">entschuldigen</button>
                <button type="button" class="btn btn-outline-danger" data-status="unentschuldigt">unentschuldigt</button><button type="button" class="btn btn-outline-secondary" data-status="offen">offen</button></div>` : ''}</div>
            ${eintraege.length ? `<div class="table-responsive" style="max-height:560px"><table class="table table-sm table-card align-middle mb-0"><thead><tr>${f.verwalten ? '<th><input type="checkbox" class="form-check-input" data-alle-wahl aria-label="Alle"></th>' : ''}<th>Datum</th><th>Std.</th>${auswahl ? '' : '<th>Name</th>'}<th>Fach</th><th>Art</th><th>Status</th><th>Lehrkraft</th></tr></thead><tbody>
              ${eintraege.map(x => `<tr>${f.verwalten ? `<td><input type="checkbox" class="form-check-input" data-id="${x.id}" aria-label="auswählen"></td>` : ''}<td class="text-nowrap">${e(datumKurz(x.datum))}</td><td>${x.stunde}.</td>
                ${auswahl ? '' : `<td>${e(namen.get(x.schuelerId) ?? '?')}</td>`}<td>${e(x.fach)}</td>
                <td>${x.art === 'verspaetet' ? `<span class="text-warning-emphasis">verspätet ${x.minuten} min</span>` : 'fehlt'}${x.bemerkung ? `<div class="small text-body-secondary">${e(x.bemerkung)}</div>` : ''}</td>
                <td>${STATUS_BADGE[x.status] ?? ''}</td><td class="small text-body-secondary">${e(x.lehrer)}</td></tr>`).join('')}
              </tbody></table></div>` : `<div class="card-body py-0">${leer('Keine Fehlzeiten im Zeitraum.')}</div>`}
          </div></div></div>`}`;
      const neu = (v, b) => kontext.navigieren(`${ziel(klasse, 'fehlzeiten', b)}/${v}`);
      inhalt.querySelector('[data-von]').onchange = ev => neu(ev.target.value, datum);
      inhalt.querySelector('[data-bis]').onchange = ev => neu(von, ev.target.value);
      inhalt.querySelectorAll('[data-s]').forEach(tr => tr.onclick = () => { auswahl = Number(tr.dataset.s); zeichnen(); });
      inhalt.querySelector('[data-alle]')?.addEventListener('click', () => { auswahl = null; zeichnen(); });
      inhalt.querySelector('[data-alle-wahl]')?.addEventListener('change', ev => inhalt.querySelectorAll('[data-id]').forEach(c => { c.checked = ev.target.checked; }));
      inhalt.querySelectorAll('[data-status]').forEach(b => b.onclick = async () => {
        const ids = [...inhalt.querySelectorAll('[data-id]:checked')].map(c => Number(c.dataset.id));
        if (!ids.length) return toast('Bitte zuerst Einträge auswählen.', true);
        try { await senden('/api/app/klassenbuch/fehlzeiten/status', { ids, status: b.dataset.status }); toast(`${ids.length} ${ids.length === 1 ? 'Eintrag' : 'Einträge'} geändert.`); neuLaden(); }
        catch (err) { toast(err.message, true); }
      });
      inhalt.querySelector('[data-tage]')?.addEventListener('click', () => fehlenDialog(f.schueler, auswahl, neuLaden));
    };
    zeichnen();
    return;
  }

  if (reiter === 'vermerke') {
    const [v, schueler] = await Promise.all([
      holen(`/api/app/klassenbuch/vermerke?klasse=${q}&von=${plusTage(heuteIso(), -365)}&bis=${plusTage(heuteIso(), 30)}`),
      holen(`/api/app/klassenbuch/schueler?klasse=${q}`),
    ]);
    const namen = new Map(schueler.map(x => [x.id, `${x.name}, ${x.vorname}`]));
    inhalt.innerHTML = `<div class="card"><div class="card-header d-flex align-items-center">Vermerke · ${e(klasse)}<button type="button" class="btn btn-sm btn-primary ms-auto" data-neu>${icon('plus', 'me-1')}Neuer Vermerk</button></div>
      ${v.vermerke.length ? `<ul class="list-group list-group-flush">${v.vermerke.map(x => `<li class="list-group-item d-flex gap-3 align-items-start">
          <div class="text-nowrap small text-body-secondary" style="width:80px">${e(datumKurz(x.datum))}${x.datum.slice(0, 4)}</div>
          <span class="badge ${VERMERK_FARBE[x.art] ?? 'text-bg-secondary'} fw-normal">${e(x.art)}</span>
          <div class="flex-grow-1">${x.schuelerId ? `<b>${e(namen.get(x.schuelerId) ?? '')}:</b> ` : ''}${e(x.text)}</div>
          <span class="small text-body-secondary">${e(x.lehrer)}</span>
          <button type="button" class="btn btn-sm btn-link text-danger p-0" data-weg="${x.id}" title="Löschen">${icon('loeschen')}</button></li>`).join('')}</ul>`
        : `<div class="card-body py-0">${leer('Noch keine Vermerke.')}</div>`}</div>`;
    inhalt.querySelector('[data-neu]').onclick = () => vermerkDialog(klasse, heuteIso(), neuLaden);
    inhalt.querySelectorAll('[data-weg]').forEach(b => b.onclick = async () => {
      if (!await bestaetigen('Vermerk löschen', 'Den Vermerk endgültig löschen?')) return;
      try { await api(`/api/app/klassenbuch/vermerke/${b.dataset.weg}`, { methode: 'DELETE' }); neuLaden(); } catch (err) { toast(err.message, true); }
    });
    return;
  }

  // Inhalte & Hausaufgaben
  inhalt.innerHTML = `<div class="card"><div class="card-header d-flex flex-wrap align-items-center gap-2">Inhalte & Hausaufgaben
      <div class="ms-auto d-flex align-items-center gap-2 fw-normal small">
        <input type="date" class="form-control form-control-sm" data-von value="${plusTage(heuteIso(), -28)}" aria-label="von"><span>bis</span>
        <input type="date" class="form-control form-control-sm" data-bis value="${plusTage(heuteIso(), 14)}" aria-label="bis"></div></div>
    <div data-verlauf>${lade()}</div></div>`;
  const verlaufLaden = async () => {
    const von = inhalt.querySelector('[data-von]').value, bis = inhalt.querySelector('[data-bis]').value;
    inhalt.querySelector('[data-verlauf]').innerHTML = verlaufTabelle(await holen(`/api/app/klassenbuch/klasse?klasse=${q}&von=${von}&bis=${bis}`), true);
  };
  inhalt.querySelector('[data-von]').onchange = verlaufLaden;
  inhalt.querySelector('[data-bis]').onchange = verlaufLaden;
  await verlaufLaden();
}

const VERMERK_FARBE = { Lob: 'text-bg-success', 'Ordnungsmaßnahme': 'text-bg-danger', 'Elterngespräch': 'text-bg-info', 'Hausaufgabe fehlt': 'text-bg-warning', 'Material vergessen': 'text-bg-warning' };
const STATUS_BADGE = {
  offen: '<span class="badge text-bg-warning fw-normal">offen</span>',
  entschuldigt: '<span class="badge text-bg-success fw-normal">entschuldigt</span>',
  unentschuldigt: '<span class="badge text-bg-danger fw-normal">unentschuldigt</span>',
};

async function vermerkDialog(klasse, datum, fertig) {
  const [v, schueler] = await Promise.all([
    holen(`/api/app/klassenbuch/vermerke?klasse=${encodeURIComponent(klasse)}&von=${datum}&bis=${datum}`),
    holen(`/api/app/klassenbuch/schueler?klasse=${encodeURIComponent(klasse)}`),
  ]);
  dialog({
    titel: `Vermerk · ${klasse}`,
    inhalt: `<div class="row g-3">
      <div class="col-sm-5"><label class="form-label small">Datum</label><input type="date" class="form-control" name="datum" value="${datum}"></div>
      <div class="col-sm-7"><label class="form-label small">Art</label><select class="form-select" name="art">${v.arten.map(a => `<option>${e(a)}</option>`).join('')}</select></div>
      <div class="col-12"><label class="form-label small">Betrifft</label><select class="form-select" name="schueler"><option value="">ganze Klasse</option>
        ${schueler.map(x => `<option value="${x.id}">${e(x.name)}, ${e(x.vorname)}</option>`).join('')}</select></div>
      <div class="col-12"><label class="form-label small">Text</label><textarea class="form-control" name="text" rows="3" required></textarea></div></div>`,
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
      const f = n => box.querySelector(`[name=${n}]`).value;
      await senden('/api/app/klassenbuch/vermerke', { datum: f('datum'), klasse, schuelerId: f('schueler') ? Number(f('schueler')) : null, art: f('art'), text: f('text') });
      toast('Vermerk gespeichert.');
      fertig();
    } }],
  });
}

function fehlenDialog(schueler, vorwahl, fertig) {
  dialog({
    titel: 'Fehlen eintragen',
    inhalt: `<p class="small text-body-secondary">Trägt für alle Unterrichtsstunden der gewählten Tage eine Fehlzeit ein (z. B. nach einer Krankmeldung).</p>
      <div class="row g-3">
      <div class="col-12"><label class="form-label small">Schüler/in</label><select class="form-select" name="schueler">${schueler.map(x => `<option value="${x.id}" ${x.id === vorwahl ? 'selected' : ''}>${e(x.name)}, ${e(x.vorname)}</option>`).join('')}</select></div>
      <div class="col-6"><label class="form-label small">von</label><input type="date" class="form-control" name="von" value="${heuteIso()}"></div>
      <div class="col-6"><label class="form-label small">bis</label><input type="date" class="form-control" name="bis" value="${heuteIso()}"></div>
      <div class="col-sm-5"><label class="form-label small">Status</label><select class="form-select" name="status"><option value="offen">offen</option><option value="entschuldigt">entschuldigt</option><option value="unentschuldigt">unentschuldigt</option></select></div>
      <div class="col-sm-7"><label class="form-label small">Bemerkung</label><input class="form-control" name="bemerkung" placeholder="z. B. Krankmeldung der Eltern"></div></div>`,
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Eintragen', art: 'haupt', aktion: async box => {
      const f = n => box.querySelector(`[name=${n}]`).value;
      const r = await senden('/api/app/klassenbuch/fehlzeiten/tage', { schuelerId: Number(f('schueler')), von: f('von'), bis: f('bis'), status: f('status'), bemerkung: f('bemerkung') });
      toast(`${r.stunden} Fehlstunden eingetragen.`);
      fertig();
    } }],
  });
}
