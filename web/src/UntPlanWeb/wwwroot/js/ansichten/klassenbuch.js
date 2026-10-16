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
  kontext.titel(!ich.kollegium ? 'Hausaufgaben' : reiterParam === 'fehlzeiten' ? 'Fehlzeiten' : klasseParam && reiterParam !== 'uebersicht' ? 'Klassenbuch' : '');
  const el = await kontext.elemente();
  // ohne Angabe: eigene Klasse (Klassenleitung), sonst die erste
  const eigene = el.klassen.find(k => ich.element && (k.leitung ?? []).some(l => l.toLowerCase() === ich.element.toLowerCase()))?.kurz;
  const klasse = ich.kollegium ? (klasseParam && klasseParam !== '-' ? klasseParam : eigene ?? el.klassen[0]?.kurz) : ich.element;

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

  if (!klasseParam || reiterParam === 'uebersicht') return uebersicht(seite, kontext, klasseParam && klasseParam !== '-' ? klasseParam : '');
  const reiter = ['woche', 'fehlzeiten', 'vermerke', 'verlauf'].includes(reiterParam) ? reiterParam : 'woche';
  const datum = /^\d{4}-\d{2}-\d{2}$/.test(datumParam ?? '') ? datumParam : heuteIso();
  const ziel = (k, r, d = datum) => `#/klassenbuch/${encodeURIComponent(k ?? '')}/${r}/${d}`;
  const REITER = { woche: ['calendar3-week', 'Wochenübersicht'], fehlzeiten: ['person-x', 'Fehlzeiten'], vermerke: ['chat-left-text', 'Vermerke'], verlauf: ['journal-text', 'Lehrstoff & Hausaufgaben'] };
  seite.innerHTML = `
    <div class="u-filter mb-2"><label class="u-feld"><span>Klasse</span><span data-wahl></span></label></div>
    <ul class="nav nav-tabs mb-3">${Object.entries(REITER).map(([r, [ic, t]]) => `<li class="nav-item"><a class="nav-link ${r === reiter ? 'active' : ''}" href="${ziel(klasse, r)}"><i class="bi bi-${ic}"></i>${t}</a></li>`).join('')}</ul>
    <div data-reiterinhalt>${lade()}</div>`;
  if (klasse) seite.querySelector('[data-wahl]').replaceWith(elementWahl({ eintraege: el.klassen, wert: klasse, platzhalter: 'Klasse wählen', beiWahl: k => kontext.navigieren(ziel(k, reiter)) }));
  const inhalt = seite.querySelector('[data-reiterinhalt]');
  const neuLaden = () => kontext.navigieren(location.hash);
  const k2 = { ...kontext, nachSpeichern: neuLaden };

  const offenLaden = async () => { // (Offene Stunden haben eine eigene Seite)
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
    // Darstellungsmodus: neue Ansicht oder alte Liste (grau gestreift wie im alten Klassenbuch), je Gerät gemerkt
    let modus = 'neu';
    try { modus = localStorage.getItem('untplan:fehlzeiten-modus') === 'alt' ? 'alt' : 'neu'; } catch { /* privat */ }
    const zeichnen = () => {
      const eintraege = auswahl ? f.eintraege.filter(x => x.schuelerId === auswahl) : f.eintraege;
      if (modus === 'alt') {
        inhalt.innerHTML = `<div class="d-flex flex-wrap align-items-center gap-2 mb-3 small">
            <span>Zeitraum</span><input type="date" class="form-control form-control-sm" style="width:auto" data-von value="${von}"><span>bis</span>
            <input type="date" class="form-control form-control-sm" style="width:auto" data-bis value="${datum}">
            <select class="form-select form-select-sm" style="width:auto" data-schuelerwahl aria-label="Schüler*in"><option value="">- Alle -</option>${f.schueler.map(x => `<option value="${x.id}" ${auswahl === x.id ? 'selected' : ''}>${e(x.name)} ${e(x.vorname)}</option>`).join('')}</select>
            <div class="btn-group btn-group-sm ms-auto" role="group" aria-label="Darstellung"><button type="button" class="btn btn-outline-secondary" data-modus="neu">Neue Ansicht</button><button type="button" class="btn btn-outline-secondary active" data-modus="alt">Alte Liste</button></div></div>
          <div class="u-tabelle-karte p-3">${eintraege.length ? `<table class="alt-liste w-100"><thead><tr><th class="mitte">Auswahl<br><input type="checkbox" data-alle-wahl aria-label="Alle"></th><th>Schüler*innen</th><th>Klasse</th><th>Datum</th><th>Std.</th><th>Fach</th><th>Abwesenheitsgrund</th><th>Status</th><th>Text</th><th>Lehrkraft</th></tr></thead><tbody>
            ${eintraege.map(x => `<tr><td class="mitte"><input type="checkbox" data-id="${x.id}" aria-label="auswählen"></td><td>${e(namen.get(x.schuelerId) ?? '?')}</td><td>${e(x.klasse)}</td><td>${e(datumKurz(x.datum))}</td><td class="mitte">${x.stunde}</td><td>${e(x.fach)}</td>
              <td>${x.art === 'verspaetet' ? `verspätet ${x.minuten} min` : x.art === 'vorzeitig' ? `vorzeitig gegangen ${x.minuten} min` : e(x.grund || 'fehlt')}</td><td>${e(x.status)}</td><td class="text">${e(x.bemerkung)}</td><td>${e(x.lehrer)}</td></tr>`).join('')}</tbody></table>
            ${f.verwalten ? `<div class="d-flex gap-2 mt-3"><button type="button" class="sd-pille klein" data-status="entschuldigt">Entschuldigen</button><button type="button" class="sd-pille klein" data-status="unentschuldigt">Unentschuldigt</button><button type="button" class="sd-pille klein" data-status="offen">Offen</button>${f.verwalten ? '<button type="button" class="sd-pille klein" data-tage>Neu</button>' : ''}</div>` : ''}`
            : leer('Keine Fehlzeiten im Zeitraum.')}</div>`;
        binden();
        inhalt.querySelector('[data-schuelerwahl]').onchange = ev => { auswahl = ev.target.value ? Number(ev.target.value) : null; zeichnen(); };
        return;
      }
      inhalt.innerHTML = `<div class="d-flex flex-wrap align-items-center gap-2 mb-3 small">
          <span>Zeitraum</span><input type="date" class="form-control form-control-sm" style="width:auto" data-von value="${von}"><span>bis</span>
          <input type="date" class="form-control form-control-sm" style="width:auto" data-bis value="${datum}">
          <div class="btn-group btn-group-sm ms-auto" role="group" aria-label="Darstellung"><button type="button" class="btn btn-outline-secondary active" data-modus="neu">Neue Ansicht</button><button type="button" class="btn btn-outline-secondary" data-modus="alt">Alte Liste</button></div>
          ${f.verwalten ? `<button type="button" class="btn btn-sm btn-primary" data-tage>${icon('plus', 'me-1')}Fehlen eintragen (ganze Tage)</button>` : ''}</div>
        ${!f.schueler.length ? alarm('secondary', 'Für diese Klasse wurde keine Schülerliste aus UntPlan übertragen.') : (() => {
          // nur wer im Zeitraum tatsächlich gefehlt hat (oder verspätet / vorzeitig gegangen ist)
          const mit = f.schueler.filter(x => x.stunden || x.verspaetungen || x.vorzeitig);
          const ini = x => ((x.name?.[0] ?? '') + (x.vorname?.[0] ?? '')).toUpperCase();
          const chips = x => [
            x.stunden ? `<span class="nl-chip rot" title="Fehlstunden">${x.stunden} Std.</span>` : '',
            x.tage ? `<span class="nl-chip grau" title="Fehltage">${x.tage} ${x.tage === 1 ? 'Tag' : 'Tage'}</span>` : '',
            x.offen ? `<span class="nl-chip gelb" title="noch nicht entschuldigt">${x.offen} offen</span>` : '',
            x.entschuldigt ? `<span class="nl-chip gruen" title="entschuldigt">${x.entschuldigt} entsch.</span>` : '',
            x.unentschuldigt ? `<span class="nl-chip rot" title="unentschuldigt">${x.unentschuldigt} unentsch.</span>` : '',
            x.verspaetungen ? `<span class="nl-chip gelb" title="Verspätungen">${x.verspaetungen}× versp. (${x.minuten}′)</span>` : '',
            x.vorzeitig ? `<span class="nl-chip lila" title="vorzeitig gegangen">${x.vorzeitig}× vorz. (${x.vorzeitigMinuten}′)</span>` : '',
            f.regeln?.minutenUmrechnen && x.stundenAusMinuten ? `<span class="nl-chip grau" title="Fehlstunden aus Minuten (je ${f.regeln.minutenProFehlstunde} min)">+${x.stundenAusMinuten} Std.</span>` : '',
          ].filter(Boolean).join(' ');
          const artText = x => x.art === 'verspaetet' ? `<span class="nl-chip gelb">verspätet ${x.minuten} min</span>` : x.art === 'vorzeitig' ? `<span class="nl-chip lila">vorzeitig ${x.minuten} min</span>` : '<span class="nl-chip rot">fehlt</span>';
          return `
        <div class="nl-zahlen"><span class="nl-zahl"><b>${mit.length}</b> von ${f.schueler.length} Schüler*innen mit Fehlzeiten</span>
          <span class="nl-zahl rot"><b>${mit.reduce((n, x) => n + x.stunden, 0)}</b> Fehlstunden</span>
          <span class="nl-zahl gelb"><b>${mit.reduce((n, x) => n + x.offen, 0)}</b> offen</span>
          <span class="nl-zahl gelb"><b>${mit.reduce((n, x) => n + x.verspaetungen, 0)}</b> Verspätungen</span></div>
        <div class="row g-3"><div class="col-xl-6"><div class="nl-tabelle">${mit.length ? `<table><thead><tr><th colspan="2">Name</th><th>Fehlzeiten</th></tr></thead><tbody>
            ${mit.map(x => `<tr role="button" data-s="${x.id}" class="${auswahl === x.id ? 'nl-gewaehlt' : ''}"><td class="nl-avatar-zelle"><span class="nl-avatar ${x.offen || x.unentschuldigt ? 'voll' : ''}">${e(ini(x))}</span></td>
              <td><div class="nl-name">${e(x.name)} ${e(x.vorname)}</div>${x.hinweis ? `<div class="nl-unter text-danger"><i class="bi bi-exclamation-triangle-fill me-1"></i>${e(x.hinweis)}</div>` : ''}</td>
              <td><div class="nl-chips">${chips(x)}</div></td></tr>`).join('')}</tbody></table>` : `<div class="p-3">${leer('Im Zeitraum hat niemand gefehlt.', 'person-check')}</div>`}</div></div>
          <div class="col-xl-6"><div class="nl-tabelle"><div class="nl-kopf">${auswahl ? e(namen.get(auswahl)) : 'Alle Einträge'}
              ${auswahl ? '<button type="button" class="btn btn-sm btn-link py-0" data-alle>alle anzeigen</button>' : ''}
              ${f.verwalten && eintraege.length ? `<span class="ms-auto d-flex gap-2"><button type="button" class="nl-knopf gruen" data-status="entschuldigt">Entschuldigen</button>
                <button type="button" class="nl-knopf rot" data-status="unentschuldigt">Unentschuldigt</button><button type="button" class="nl-knopf" data-status="offen">Offen</button></span>` : ''}</div>
            ${eintraege.length ? `<div style="max-height:620px;overflow:auto"><table><thead><tr>${f.verwalten ? '<th class="nl-wahl"><input type="checkbox" class="nl-rund" data-alle-wahl aria-label="Alle"></th>' : ''}<th>Datum</th>${auswahl ? '' : '<th>Name</th>'}<th>Fach</th><th>Art</th><th>Status</th></tr></thead><tbody>
              ${eintraege.map(x => `<tr>${f.verwalten ? `<td class="nl-wahl"><input type="checkbox" class="nl-rund" data-id="${x.id}" aria-label="auswählen"></td>` : ''}<td class="text-nowrap"><div>${e(datumKurz(x.datum))}</div><div class="nl-unter">${x.stunde}. Std.</div></td>
                ${auswahl ? '' : `<td>${e(namen.get(x.schuelerId) ?? '?')}</td>`}<td>${e(x.fach)}<div class="nl-unter">${e(x.lehrer)}</div></td>
                <td>${artText(x)}${x.bemerkung ? `<div class="nl-unter">${e(x.bemerkung)}</div>` : ''}</td>
                <td>${STATUS_BADGE[x.status] ?? ''}</td></tr>`).join('')}
              </tbody></table></div>` : `<div class="p-3">${leer('Keine Fehlzeiten im Zeitraum.')}</div>`}
          </div></div></div>`;
        })()}`;
      binden();
    };
    const binden = () => {
      inhalt.querySelectorAll('[data-modus]').forEach(b => b.onclick = () => { modus = b.dataset.modus; try { localStorage.setItem('untplan:fehlzeiten-modus', modus); } catch { /* privat */ } zeichnen(); });
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

// ------------------------------------------------------------------ Übersicht wie im Original (UW-09)

const MONATE_LANG = ['Januar', 'Februar', 'März', 'April', 'Mai', 'Juni', 'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember'];
const WT = ['Montag', 'Dienstag', 'Mittwoch', 'Donnerstag', 'Freitag', 'Samstag', 'Sonntag'];
const tagLang = iso => { const d = new Date(iso + 'T12:00'); return `${WT[(d.getDay() + 6) % 7]} | ${d.getDate()}. ${MONATE_LANG[d.getMonth()]} ${d.getFullYear()}`; };

/**
 * Klassenbuch → Übersicht: oben Suche mit Element-Chip (eigene Lehrkraft oder Klasse), darunter je Tag die Stunden mit Lehrstoff,
 * Abwesenheiten und Hausaufgaben. „Scrollen um die letzten 7 Tage zu laden“ holt ältere Tage nach.
 */
async function uebersicht(seite, kontext, klasse) {
  const ich = kontext.ich;
  const el = await kontext.elemente();
  const sj = kontext.schuljahr;
  let element = klasse ? { typ: 'klasse', kurz: klasse } : ich.element ? { typ: 'lehrer', kurz: ich.element } : el.klassen[0] ? { typ: 'klasse', kurz: el.klassen[0].kurz } : null;
  const heute = heuteIso();
  let bis = sj?.bis && sj.bis < heute ? sj.bis : heute;
  const tage = [];
  let filter = 'alle';

  const w = kontext.werkzeug(`<div class="u-suche">${icon('suchen')}
      ${element ? `<span class="u-chip-dunkel">${e(element.kurz)}<button type="button" data-chip-weg aria-label="${e(element.kurz)} entfernen"><i class="bi bi-x-lg"></i></button></span>` : ''}
      <input type="search" data-suche placeholder="Klasse oder Lehrkraft" list="kb-elemente" aria-label="Klasse oder Lehrkraft suchen">
      <button type="button" class="u-leeren" data-leeren title="Suche leeren" aria-label="Suche leeren"><i class="bi bi-x-lg"></i></button>
      <span class="u-trenn"></span>
      <select data-filter aria-label="Anzeigen"><option value="alle">Alle</option><option value="offen">Offen</option><option value="lehrstoff">Mit Lehrstoff</option></select></div>
    <datalist id="kb-elemente">${el.klassen.map(k => `<option value="${e(k.kurz)}">Klasse</option>`).join('')}${el.lehrer.map(l => `<option value="${e(l.kurz)}">${e(l.name)}</option>`).join('')}</datalist>`);
  seite.innerHTML = '<div class="kb-uebersicht"><button type="button" class="kb-mehr" data-mehr>Scrollen um die letzten 7 Tage zu laden</button><div data-tage></div></div>';
  const ziel = seite.querySelector('[data-tage]');
  const k2 = { ...kontext, nachSpeichern: () => kontext.navigieren(location.hash) };
  const alle = [];

  const zeichnen = () => {
    alle.length = 0;
    const sichtbar = tage.map(t => ({ ...t, stunden: t.stunden.filter(s => filter === 'alle' || (filter === 'offen' ? !s.lehrstoff || !s.geprueft : !!s.lehrstoff)) })).filter(t => t.stunden.length);
    let letzteKw = null;
    ziel.innerHTML = sichtbar.length ? sichtbar.map(t => {
      const kw = t.kw !== letzteKw ? `KW ${t.kw}` : '';
      letzteKw = t.kw;
      return `<section class="kb-tag-gruppe"><div class="kb-tagkopf"><span class="kw">${kw}</span><span class="linie"></span>
          <button type="button" class="kb-tagpille" data-zu="${t.datum}" aria-expanded="true">${e(tagLang(t.datum))} <i class="bi bi-chevron-down"></i></button><span class="linie"></span></div>
        <div class="u-tabelle-karte kb-tagliste" data-tagliste="${t.datum}"><table class="u-tabelle"><thead><tr><th style="width:44px">h</th><th style="width:110px">Fach</th><th style="width:130px">Klassen</th><th>Lehrstoff</th><th style="width:210px">Abwesenheiten</th><th>Hausaufgaben</th></tr></thead><tbody>
          ${t.stunden.map(s => { alle.push({ ...s, datum: t.datum }); const offen = s.abwesend - s.entschuldigt; return `<tr role="button" data-s="${alle.length - 1}" class="${s.status === 'entfall' ? 'entfall' : ''}">
            <td class="fw-bold">${s.von === s.bis ? s.von : `${s.von}-${s.bis}`}</td><td>${e(s.fach)}</td><td>${s.klassen.map(e).join(',<br>')}</td>
            <td class="kb-stoff" title="${e(s.lehrstoff)}">${e(s.lehrstoff)}</td>
            <td>${s.geprueft || s.abwesend ? `<span class="kb-abwpille ${offen ? '' : 'ok'}"><b>${offen}</b><span>|</span><b>${s.entschuldigt}</b> Entschuldigt</span>` : ''}</td>
            <td class="kb-stoff" title="${e(s.hausaufgaben)}">${e(s.hausaufgaben)}</td></tr>`; }).join('')}
        </tbody></table></div></section>`;
    }).join('') : `<div class="u-tabelle-karte">${leer('Im Zeitraum gibt es keine Stunden.', 'journal-text')}</div>`;
    ziel.querySelectorAll('[data-zu]').forEach(b => b.onclick = () => {
      const l = ziel.querySelector(`[data-tagliste="${b.dataset.zu}"]`);
      l.classList.toggle('d-none');
      b.setAttribute('aria-expanded', String(!l.classList.contains('d-none')));
    });
    ziel.querySelectorAll('[data-s]').forEach(tr => tr.onclick = () => {
      const s = alle[Number(tr.dataset.s)];
      const karten = alle.map(x => ({ datum: x.datum, von: x.von, bis: x.bis, beginn: x.beginn, ende: x.ende, status: x.status, fach: x.fach, fachName: x.fach, art: '', text: '',
        klassen: x.klassen.map(k => ({ k })), lehrer: x.lehrer.map(k => ({ k })), raeume: x.raeume.map(k => ({ k })), unterricht: x.unterricht }));
      karteDialog(karten[alle.indexOf(s)], { ...k2, liste: karten });
    });
  };

  let laedt = false;
  const laden = async (erst = false) => {
    if (!element || laedt) return;
    laedt = true;
    const hoeheVorher = document.documentElement.scrollHeight;
    try {
      const r = await holen(`/api/app/klassenbuch/uebersicht?bis=${bis}&tage=7&${element.typ}=${encodeURIComponent(element.kurz)}`);
      tage.unshift(...r.tage.slice().reverse());
      bis = plusTage(r.von, -1);
      zeichnen();
      if (erst) window.scrollTo(0, document.documentElement.scrollHeight);
      else window.scrollTo(0, document.documentElement.scrollHeight - hoeheVorher);
    } catch (err) { kontext.fehler(err); }
    laedt = false;
    if (sj?.von && bis < sj.von) seite.querySelector('[data-mehr]').remove();
  };
  if (!element) { ziel.innerHTML = leer('Bitte eine Klasse oder Lehrkraft wählen.'); return; }
  await laden(true);
  if (tage.length < 3) await laden();
  seite.querySelector('[data-mehr]')?.addEventListener('click', () => laden());
  const beimScrollen = () => {
    if (!document.body.contains(seite) || location.hash.split('/')[1] !== 'klassenbuch') { window.removeEventListener('scroll', beimScrollen); return; }
    if (window.scrollY < 40) laden();
  };
  setTimeout(() => window.addEventListener('scroll', beimScrollen, { passive: true }), 400);

  const wechseln = kurz => {
    const k = el.klassen.find(x => x.kurz.toLowerCase() === kurz.toLowerCase());
    const l = el.lehrer.find(x => x.kurz.toLowerCase() === kurz.toLowerCase());
    if (k) kontext.navigieren(`#/klassenbuch/${encodeURIComponent(k.kurz)}/uebersicht`);
    else if (l) { element = { typ: 'lehrer', kurz: l.kurz }; tage.length = 0; bis = heute; w.querySelector('.u-chip-dunkel')?.remove(); w.querySelector('[data-suche]').insertAdjacentHTML('beforebegin', `<span class="u-chip-dunkel">${e(l.kurz)}</span>`); laden(true); }
    else toast('Keine passende Klasse oder Lehrkraft.', true);
  };
  const suche = w.querySelector('[data-suche]');
  suche.onkeydown = ev => { if (ev.key === 'Enter' && suche.value.trim()) { ev.preventDefault(); wechseln(suche.value.trim()); } };
  suche.onchange = () => { if (suche.value.trim()) wechseln(suche.value.trim()); };
  w.querySelector('[data-chip-weg]')?.addEventListener('click', () => kontext.navigieren('#/klassenbuch'));
  w.querySelector('[data-leeren]').onclick = () => { suche.value = ''; };
  w.querySelector('[data-filter]').onchange = ev => { filter = ev.target.value; zeichnen(); };
}
