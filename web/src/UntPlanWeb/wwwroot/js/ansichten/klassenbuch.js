// Klassenbuch: Wochenübersicht mit Anwesenheit, Fehlzeitenverwaltung, Vermerke, Inhalte/Hausaufgaben; Hausaufgaben für Klassenkonten.
import { e, icon, api, holen, senden, toast, dialog, bestaetigen, alarm, datumKurz, datumLang, heuteIso, plusTage, leer, lade, infoHinweis } from '../ui.js';
import { karteDialog } from '../plangitter.js';
import { elementWahl } from '../elementwahl.js';
import { detailFelder, detailWerte, NACHWEISE } from './abwesenheiten.js';
import { statusLabel, zaehltKaestchen } from './familie.js';

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

  if (!klasseParam || reiterParam === 'uebersicht') return uebersicht(seite, kontext, klasseParam && klasseParam !== '-' ? klasseParam : '', datumParam, vonParam);
  const reiter = ['woche', 'fehlzeiten', 'vermerke', 'verlauf'].includes(reiterParam) ? reiterParam : 'woche';
  const datum = /^\d{4}-\d{2}-\d{2}$/.test(datumParam ?? '') ? datumParam : heuteIso();
  const ziel = (k, r, d = datum) => `#/klassenbuch/${encodeURIComponent(k ?? '')}/${r}/${d}`;
  const REITER = { woche: ['calendar3-week', 'Wochenübersicht'], fehlzeiten: ['person-x', 'Fehlzeiten'], vermerke: ['chat-left-text', 'Vermerke'], verlauf: ['journal-text', 'Lehrstoff & Hausaufgaben'] };
  seite.innerHTML = `
    <div class="u-filter mb-2"><label class="u-feld"><span>Klasse</span><span data-wahl></span></label></div>
    ${reiter === 'fehlzeiten' ? '' : `<ul class="nav nav-tabs mb-3">${Object.entries(REITER).map(([r, [ic, t]]) => `<li class="nav-item"><a class="nav-link ${r === reiter ? 'active' : ''}" href="${ziel(klasse, r)}"><i class="bi bi-${ic}"></i>${t}</a></li>`).join('')}</ul>`}
    <div data-reiterinhalt>${lade()}</div>`;
  if (klasse) seite.querySelector('[data-wahl]').replaceWith(elementWahl({ eintraege: el.klassen, wert: klasse, platzhalter: 'Klasse wählen', beiWahl: k => kontext.navigieren(ziel(k, reiter)) }));
  const inhalt = seite.querySelector('[data-reiterinhalt]');
  const neuLaden = () => kontext.navigieren(location.hash);
  const k2 = { ...kontext, nachSpeichern: neuLaden };

  if (!klasse) { inhalt.innerHTML = leer('Keine Klassen vorhanden.'); return; }
  const q = encodeURIComponent(klasse);

  if (reiter === 'woche') return wochenraster(inhalt, kontext, klasse, datum, ziel, k2, neuLaden);

  if (reiter === 'fehlzeiten') {
    // Fehlzeiten der Klasse: nur echte Fehlzeiten (Verspätungen stehen unter Abwesenheiten), eine Tabelle – je Schüler*in eine Zeile,
    // aufklappbar mit den einzelnen Fehlstunden (Auswahl, Status setzen, Details bearbeiten)
    const von = /^\d{4}-\d{2}-\d{2}$/.test(vonParam ?? '') ? vonParam : plusTage(datum, -90);
    const f = await holen(`/api/app/klassenbuch/fehlzeiten?klasse=${q}&von=${von}&bis=${datum}`);
    f.gruende = Object.fromEntries((await holen('/api/app/abwesenheitsgruende').catch(() => [])).map(g => [g.kurz, g.name]));
    const fehlt = f.eintraege.filter(x => x.art === 'fehlt');
    const offen = new Set();
    const ini = x => ((x.name?.[0] ?? '') + (x.vorname?.[0] ?? '')).toUpperCase();
    const zeilen = f.schueler.map(x => {
      const eig = fehlt.filter(y => y.schuelerId === x.id);
      return { ...x, eig, stunden: eig.length, tage: (f.zeilen ?? []).filter(z => z.schuelerId === x.id).reduce((n, z) => n + z.fehltage, 0), offen: eig.filter(y => y.status === 'offen').length,
        entschuldigt: eig.filter(y => y.status === 'entschuldigt').length, unentschuldigt: eig.filter(y => y.status === 'unentschuldigt').length };
    }).filter(x => (f.zeilen ?? []).some(z => z.schuelerId === x.id)).sort((a, b) => b.stunden - a.stunden || a.name.localeCompare(b.name, 'de'));
    // Fehlzeiten je Unterrichtsstunde (Datum, Zeit, Fach, Lehrkraft, Fehltage, Fehlstd., Fehlmin., zählt, Grund, Status, Text) wie in der Fehlzeitenliste
    const grundName = g => f.gruende?.[g] ?? g;
    // Untertabelle wie die Fehlzeitenliste im Original: leicht eingerückt auf hellgrauer Fläche, feine Linien, Wochentag grau vor dem Datum;
    // Abwesenheitsgrund und Text nur, wenn mindestens eine Stunde sie enthält
    const WT = ['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'];
    const wtKurz = iso => WT[new Date(`${iso}T12:00:00`).getDay()] ?? '';
    const stundenTabelle = sid => {
      const zl = (f.zeilen ?? []).filter(z => z.schuelerId === sid);
      const mitGrund = zl.some(z => z.grund);
      const zusatz = y => `${nachweis(y)}${y.elternInformiert ? ' <span class="nl-tag"><i class="bi bi-telephone me-1"></i>Eltern</span>' : ''}${y.notizIntern ? ` <span class="nl-tag" title="${e(y.notizIntern)}"><i class="bi bi-lock me-1"></i>Notiz</span>` : ''}`;
      const mitText = zl.some(z => z.text || zusatz(f.eintraege.find(q => q.id === z.id) ?? {}));
      return `<div class="fz-unter"><table class="fz-tabelle-neu fz-eingebettet"><thead><tr>${f.verwalten ? '<th class="nl-wahl"></th>' : ''}<th>Datum</th><th>Zeit</th><th>Fach</th><th>Lehrkraft</th><th class="zahl">Fehlstd.</th><th class="zahl">Fehlmin.</th><th class="mitte">zählt</th>${mitGrund ? '<th>Abwesenheitsgrund</th>' : ''}<th>Status</th>${mitText ? '<th>Text</th>' : ''}<th></th></tr></thead><tbody>
        ${zl.map(z => { const y = f.eintraege.find(q => q.id === z.id) ?? {}; return `<tr>${f.verwalten ? `<td class="nl-wahl"><input type="checkbox" class="nl-rund" data-id="${z.id}" data-von-s="${sid}" aria-label="auswählen"></td>` : ''}
          <td class="text-nowrap"><span class="fz-grau">${e(wtKurz(z.datum))}</span> <span class="fz-datum">${e(datumKurz(z.datum))}</span></td><td class="text-nowrap fz-zeit">${e(z.beginn)} - ${e(z.ende)}${z.abwesendVon !== z.beginn || z.abwesendBis !== z.ende ? `<div class="fz-klein">abwesend ${e(z.abwesendVon)}–${e(z.abwesendBis)}</div>` : ''}</td>
          <td>${e(z.fach)}</td><td>${e(z.lehrer)}</td><td class="zahl">${z.fehlstunden}</td><td class="zahl">${z.fehlminuten}</td><td class="mitte">${zaehltKaestchen(z.zaehlt)}</td>
          ${mitGrund ? `<td>${e(grundName(z.grund))}</td>` : ''}<td>${statusLabel(z.status)}</td>${mitText ? `<td class="fz-text">${e(z.text)} ${zusatz(y)}</td>` : ''}
          <td class="text-end">${f.verwalten ? `<button type="button" class="nl-icon sichtbar" data-bearb="${z.id}" title="Details bearbeiten" aria-label="Details bearbeiten">${icon('stift')}</button>` : ''}</td></tr>`; }).join('')}</tbody></table></div>`;
    };
    const zahl = (n, kl = '') => n ? `<span class="fz-zahl ${kl}">${n}</span>` : '<span class="fz-null">–</span>';
    const nachweis = y => y.nachweis ? `<span class="nl-tag" title="${e(NACHWEISE[y.nachweis] ?? '')}${y.entschuldigtAm ? ` · eingegangen ${e(datumKurz(y.entschuldigtAm))}` : ''}"><i class="bi bi-file-earmark-check me-1"></i>${e((NACHWEISE[y.nachweis] ?? y.nachweis).replace('ärztliches ', '').replace(' Entschuldigung', ''))}</span>` : '';
    const klWahl = seite.querySelector('.u-filter .u-feld')?.lastElementChild;
    const zeichnen = () => {
      // kompakter Kopf: Klasse, Von, Bis, (i)-Hinweis und Kennzahlen in einer Zeile, rechts der Knopf
      inhalt.innerHTML = `<div class="nl-leiste fz-kopfzeile">
          <span class="nl-pille" data-klwahl><span class="me-1 text-body-secondary">Klasse</span></span>
          <label class="nl-pille"><span class="me-1 text-body-secondary">Von</span><input type="date" data-von value="${von}"></label>
          <label class="nl-pille"><span class="me-1 text-body-secondary">Bis</span><input type="date" data-bis value="${datum}"></label>
          ${infoHinweis('<b>Fehlstunden</b> entstehen aus einer Abwesenheit und dem Stundenplan: gezählt werden die Unterrichtsstunden, in denen die Schülerin bzw. der Schüler hätte anwesend sein müssen (z. B. ganzer Mittwoch abwesend, laut Plan 4 Stunden → 4 Fehlstunden). Den Zeitraum selbst erfassen Sie unter <a href="#/abwesenheiten">Abwesenheiten</a>.')}
          ${f.schueler.length ? `<span class="nl-zahlen kompakt"><span class="nl-zahl still" title="Schüler*innen mit Fehlzeiten"><b>${zeilen.length}/${f.schueler.length}</b> mit Fehlzeiten</span>
          <span class="nl-zahl rot still"><b>${fehlt.length}</b> Fehlstd.</span>
          <span class="nl-zahl gelb still"><b>${fehlt.filter(y => y.status === 'offen').length}</b> offen</span>
          <span class="nl-zahl rot still"><b>${fehlt.filter(y => y.status === 'unentschuldigt').length}</b> nicht entsch.</span></span>` : ''}
          ${f.verwalten ? `<span class="ms-auto d-flex gap-2"><button type="button" class="nl-hinzu" data-tage><i class="bi bi-plus-lg"></i>Fehlen eintragen</button></span>` : ''}</div>
        ${!f.schueler.length ? alarm('secondary', 'Für diese Klasse wurde keine Schülerliste aus UntPlan übertragen.') : `
        <div class="nl-tabelle">${zeilen.length ? `<table class="fz-tabelle"><thead><tr>${f.verwalten ? '<th class="nl-wahl"><input type="checkbox" class="nl-rund" data-alle-wahl aria-label="Alle"></th>' : ''}
            <th colspan="2">Name</th><th class="text-end">Fehlstunden</th><th class="text-end">Fehltage</th><th class="text-end">offen</th><th class="text-end">entschuldigt</th><th class="text-end">nicht entsch.</th><th></th></tr></thead><tbody>
          ${zeilen.map(x => `<tr class="fz-schueler ${offen.has(x.id) ? 'auf' : ''}" data-s="${x.id}">
              ${f.verwalten ? `<td class="nl-wahl"><input type="checkbox" class="nl-rund" data-sw="${x.id}" aria-label="alle Fehlstunden auswählen"></td>` : ''}
              <td class="nl-avatar-zelle"><span class="nl-avatar ${x.offen || x.unentschuldigt ? 'voll' : ''}">${e(ini(x))}</span></td>
              <td><div class="nl-name">${e(x.name)} ${e(x.vorname)}</div>${x.hinweis ? `<div class="nl-unter text-danger"><i class="bi bi-exclamation-triangle-fill me-1"></i>${e(x.hinweis)}</div>` : ''}</td>
              <td class="text-end">${zahl(x.stunden, 'fett')}</td><td class="text-end">${zahl(x.tage)}</td><td class="text-end">${zahl(x.offen, 'gelb')}</td>
              <td class="text-end">${zahl(x.entschuldigt, 'gruen')}</td><td class="text-end">${zahl(x.unentschuldigt, 'rot')}</td>
              <td class="text-end"><button type="button" class="fz-auf" aria-expanded="${offen.has(x.id)}" aria-label="${offen.has(x.id) ? 'Fehlstunden zuklappen' : 'Fehlstunden aufklappen'}"><i class="bi bi-chevron-down" aria-hidden="true"></i></button></td></tr>
            ${offen.has(x.id) ? `<tr class="fz-stunde"><td colspan="${f.verwalten ? 9 : 8}">${stundenTabelle(x.id)}</td></tr>` : ''}`).join('')}
          </tbody></table>
          ${f.verwalten ? `<div class="nl-fuss"><span class="nl-unter me-2">Ausgewählte Fehlstunden:</span><button type="button" class="nl-knopf gruen" data-status="entschuldigt">Entschuldigen</button>
            <button type="button" class="nl-knopf rot" data-status="unentschuldigt">Nicht entschuldigt</button><button type="button" class="nl-knopf" data-status="offen">Offen</button>
            <button type="button" class="nl-knopf" data-details>Details …</button></div>` : ''}`
          : `<div class="p-3">${leer('Im Zeitraum hat niemand gefehlt.', 'person-check')}</div>`}</div>`}`;
      // Klassenwahl aus dem Seitenkopf in die Filterzeile übernehmen
      if (klWahl) { inhalt.querySelector('[data-klwahl]').append(klWahl); seite.querySelector('.u-filter')?.classList.add('d-none'); }
      else inhalt.querySelector('[data-klwahl]')?.remove();
      binden();
    };
    const gewaehlt = () => {
      const ids = new Set([...inhalt.querySelectorAll('[data-id]:checked')].map(c => Number(c.dataset.id)));
      // ganze Schüler*in gewählt (auch zugeklappt): alle Fehlstunden
      inhalt.querySelectorAll('[data-sw]:checked').forEach(c => (f.zeilen ?? []).filter(z => z.schuelerId === Number(c.dataset.sw)).forEach(y => ids.add(y.id)));
      return [...ids];
    };
    const detailsDialog = (ids, vorlage) => dialog({ titel: ids.length === 1 ? 'Fehlstunde – Details' : `${ids.length} Fehlstunden – Details`,
      inhalt: `<div class="row g-3">${detailFelder(vorlage ?? {})}</div>`,
      knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
        await senden('/api/app/abwesenheiten', { ids, ...detailWerte(box) }, 'PUT');
        toast('Gespeichert.'); neuLaden();
      } }] });
    const binden = () => {
      const neu = (v, b) => kontext.navigieren(`${ziel(klasse, 'fehlzeiten', b)}/${v}`);
      inhalt.querySelector('[data-von]').onchange = ev => neu(ev.target.value, datum);
      inhalt.querySelector('[data-bis]').onchange = ev => neu(von, ev.target.value);
      inhalt.querySelectorAll('tr[data-s]').forEach(tr => tr.onclick = ev => {
        if (ev.target.closest('input')) return;
        const id = Number(tr.dataset.s);
        if (offen.has(id)) offen.delete(id); else offen.add(id);
        zeichnen();
      });
      inhalt.querySelectorAll('[data-sw]').forEach(c => c.onchange = () => inhalt.querySelectorAll(`[data-von-s="${c.dataset.sw}"]`).forEach(x => { x.checked = c.checked; }));
      inhalt.querySelector('[data-alle-wahl]')?.addEventListener('change', ev => inhalt.querySelectorAll('[data-id], [data-sw]').forEach(c => { c.checked = ev.target.checked; }));
      inhalt.querySelectorAll('[data-status]').forEach(b => b.onclick = async () => {
        const ids = gewaehlt();
        if (!ids.length) return toast('Bitte zuerst Fehlstunden auswählen.', true);
        try { await senden('/api/app/klassenbuch/fehlzeiten/status', { ids, status: b.dataset.status }); toast(`${ids.length} ${ids.length === 1 ? 'Fehlstunde' : 'Fehlstunden'} geändert.`); neuLaden(); }
        catch (err) { toast(err.message, true); }
      });
      inhalt.querySelector('[data-details]')?.addEventListener('click', () => {
        const ids = gewaehlt();
        if (!ids.length) return toast('Bitte zuerst Fehlstunden auswählen.', true);
        detailsDialog(ids, ids.length === 1 ? f.eintraege.find(y => y.id === ids[0]) : null);
      });
      inhalt.querySelectorAll('[data-bearb]').forEach(b => b.onclick = () => detailsDialog([Number(b.dataset.bearb)], f.eintraege.find(y => y.id === Number(b.dataset.bearb))));
      inhalt.querySelector('[data-tage]')?.addEventListener('click', () => fehlenDialog(f.schueler, null, neuLaden));
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
      <div class="col-sm-7"><label class="form-label small">Bemerkung</label><input class="form-control" name="bemerkung" placeholder="z. B. Krankmeldung der Eltern"></div>
      ${detailFelder()}</div>`,
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Eintragen', art: 'haupt', aktion: async box => {
      const f = n => box.querySelector(`[name=${n}]`).value;
      const r = await senden('/api/app/abwesenheiten', { schuelerId: Number(f('schueler')), von: f('von'), bis: f('bis'), status: f('status'), bemerkung: f('bemerkung'), ...detailWerte(box) });
      toast(`${r.stunden} Fehlstunden eingetragen.`);
      fertig();
    } }],
  });
}

// ------------------------------------------------------------------ Übersicht: Woche bzw. Tag einer Klasse oder Lehrkraft

const MONATE_KURZ = ['Jan.', 'Feb.', 'März', 'Apr.', 'Mai', 'Juni', 'Juli', 'Aug.', 'Sep.', 'Okt.', 'Nov.', 'Dez.'];
const WT = ['Montag', 'Dienstag', 'Mittwoch', 'Donnerstag', 'Freitag', 'Samstag', 'Sonntag'];
const datumVon = iso => new Date(`${iso}T12:00`);
const wtVon = iso => WT[(datumVon(iso).getDay() + 6) % 7];
const tt = iso => { const d = datumVon(iso); return `${String(d.getDate()).padStart(2, '0')}.${String(d.getMonth() + 1).padStart(2, '0')}.`; };
const kwVon = iso => { const d = datumVon(iso); d.setDate(d.getDate() + 3 - ((d.getDay() + 6) % 7)); const j = new Date(d.getFullYear(), 0, 4); return 1 + Math.round(((d - j) / 864e5 - 3 + ((j.getDay() + 6) % 7)) / 7); };
const montagVon = iso => plusTage(iso, -((datumVon(iso).getDay() + 6) % 7));
const jetztHhmm = () => { const d = new Date(); return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`; };

/** Zustand einer Stunde für Label und Filter. */
function stundenStatus(s, datum) {
  const heute = heuteIso();
  if (s.status === 'entfall') return { art: 'entfall', text: s.art || 'Entfall', farbe: 'rot' };
  const kommend = datum > heute || (datum === heute && (s.beginn || '99:99') > jetztHhmm());
  if (kommend) return { art: 'geplant', text: 'Geplant', farbe: 'grau', kommend: true };
  if (!s.lehrstoff) return { art: 'fehlt', text: 'Unvollständig', farbe: 'orange' };
  if (!s.geprueft) return { art: 'anwesenheit', text: 'Anwesenheit offen', farbe: 'gelb' };
  return { art: 'fertig', text: 'Vollständig', farbe: 'gruen' };
}

const FILTER = [
  ['alle', 'Alle Stunden'], ['fehlt', 'Lehrstoff fehlt'], ['abwesend', 'Mit Abwesenheiten'], ['eintraege', 'Mit Einträgen'], ['hausaufgaben', 'Mit Hausaufgaben'],
];
const passt = (filter, s, st) => filter === 'alle' || (filter === 'fehlt' ? st.art === 'fehlt' : filter === 'abwesend' ? s.abwesend > 0 || s.verspaetet > 0
  : filter === 'eintraege' ? s.eintraege > 0 : !!s.hausaufgaben);

/**
 * Klassenbuch → Übersicht: Filterleiste oben (Klasse bzw. Lehrkraft, Zeitraum ‹ ›, Heute, Woche/Tag), Kennzahlen mit Fortschritt
 * „Lehrstoff eingetragen“, Filter-Pillen und je Tag eine Karte mit den Stunden (Lehrstoff, Abwesenheiten, Einträge, Hausaufgaben, Status).
 * Klick auf eine Stunde öffnet die Stundendetails. Adresse: #/klassenbuch/<Klasse | @Lehrkraft | ->/uebersicht/<Datum>/<woche | tag>
 */
async function uebersicht(seite, kontext, elementParam, datumParam, modusParam) {
  const ich = kontext.ich;
  const el = await kontext.elemente();
  const heute = heuteIso();
  const modus = modusParam === 'tag' ? 'tag' : 'woche';
  const datum = /^\d{4}-\d{2}-\d{2}$/.test(datumParam ?? '') ? datumParam : heute;
  const lehrerParam = elementParam?.startsWith('@') ? elementParam.slice(1) : '';
  const klasseParam = elementParam && !lehrerParam ? elementParam : '';
  const element = klasseParam ? { typ: 'klasse', kurz: klasseParam } : lehrerParam ? { typ: 'lehrer', kurz: lehrerParam }
    : ich.element ? { typ: 'lehrer', kurz: ich.element } : el.klassen[0] ? { typ: 'klasse', kurz: el.klassen[0].kurz } : null;
  const elementCode = x => !x ? '-' : x.typ === 'lehrer' ? `@${x.kurz}` : x.kurz;
  const ziel = ({ el: x = element, d = datum, m = modus } = {}) => `#/klassenbuch/${encodeURIComponent(elementCode(x))}/uebersicht/${d}/${m}`;
  const von = modus === 'tag' ? datum : montagVon(datum);
  const bis = modus === 'tag' ? datum : plusTage(von, 6);
  const schritt = modus === 'tag' ? 1 : 7;
  const zeitraumText = modus === 'tag' ? `${wtVon(datum)}, ${tt(datum)}${datum.slice(0, 4)}`
    : `${tt(von)} – ${tt(bis)}${bis.slice(0, 4)}`;
  let filter = 'alle';
  try { filter = sessionStorage.getItem('untplan:kb-filter') || 'alle'; } catch { /* privat */ }
  if (!FILTER.some(([f]) => f === filter)) filter = 'alle';

  kontext.titel('Klassenbuch', `<a class="btn btn-outline-secondary" href="#/offen">${icon('offen')}Offene Stunden</a>`, element ? `${element.kurz} · KW ${kwVon(von)}` : '');
  const w = kontext.werkzeug(`<div class="kbu-leiste">
      <div class="kbu-wahl"><span class="kbu-wahl-titel">${element?.typ === 'lehrer' ? 'Lehrkraft' : 'Klasse'}</span><span data-wahl></span></div>
      <div class="kbu-zeit" role="group" aria-label="Zeitraum">
        <a class="kbu-pfeil" href="${ziel({ d: plusTage(von, -schritt) })}" title="${modus === 'tag' ? 'Vorheriger Tag' : 'Vorherige Woche'}" aria-label="${modus === 'tag' ? 'Vorheriger Tag' : 'Vorherige Woche'}"><i class="bi bi-chevron-left"></i></a>
        <label class="kbu-zeitraum" title="Datum wählen">${icon('kalender')}<span>${e(zeitraumText)}</span><input type="date" data-datum value="${datum}" aria-label="Datum wählen"></label>
        <a class="kbu-pfeil" href="${ziel({ d: plusTage(von, schritt) })}" title="${modus === 'tag' ? 'Nächster Tag' : 'Nächste Woche'}" aria-label="${modus === 'tag' ? 'Nächster Tag' : 'Nächste Woche'}"><i class="bi bi-chevron-right"></i></a>
        <a class="kbu-heute ${heute >= von && heute <= bis ? 'aktiv' : ''}" href="${ziel({ d: heute })}">Heute</a>
      </div>
      <div class="kbu-segment" role="group" aria-label="Ansicht">
        <a href="${ziel({ m: 'woche' })}" class="${modus === 'woche' ? 'an' : ''}" aria-current="${modus === 'woche'}">Woche</a>
        <a href="${ziel({ m: 'tag' })}" class="${modus === 'tag' ? 'an' : ''}" aria-current="${modus === 'tag'}">Tag</a>
      </div></div>`);
  const eintraege = [...el.klassen.map(k => ({ kurz: k.kurz, name: k.name && k.name !== k.kurz ? `Klasse · ${k.name}` : 'Klasse', typ: 'klasse' })),
    ...el.lehrer.map(l => ({ kurz: l.kurz, name: l.name ? `Lehrkraft · ${l.name}` : 'Lehrkraft', typ: 'lehrer' }))];
  w.querySelector('[data-wahl]').replaceWith(elementWahl({ eintraege, wert: element?.kurz, platzhalter: 'Klasse oder Lehrkraft', info: x => x.name,
    beiWahl: kurz => { const x = eintraege.find(y => y.kurz === kurz); if (x) kontext.navigieren(ziel({ el: { typ: x.typ, kurz: x.kurz } })); } }));
  const datumFeld = w.querySelector('[data-datum]');
  datumFeld.onchange = () => { if (datumFeld.value) kontext.navigieren(ziel({ d: datumFeld.value })); };
  w.querySelector('.kbu-zeitraum').addEventListener('click', ev => { if (ev.target !== datumFeld) { ev.preventDefault(); try { datumFeld.showPicker(); } catch { datumFeld.focus(); } } });

  if (!element) { seite.innerHTML = `<div class="kbu-leer">${leer('Bitte eine Klasse oder Lehrkraft wählen.', 'journal-text')}</div>`; return; }
  const r = await holen(`/api/app/klassenbuch/uebersicht?bis=${bis}&tage=${modus === 'tag' ? 1 : 7}&${element.typ}=${encodeURIComponent(element.kurz)}`);
  const tage = r.tage.slice().sort((a, b) => a.datum.localeCompare(b.datum))
    .map(t => ({ ...t, stunden: t.stunden.map(s => ({ ...s, zustand: stundenStatus(s, t.datum) })) }));
  const alle = tage.flatMap(t => t.stunden.map(s => Object.assign(s, { datum: t.datum })));
  const gehalten = alle.filter(s => s.zustand.art !== 'entfall');
  const vergangen = gehalten.filter(s => !s.zustand.kommend);
  const mitStoff = vergangen.filter(s => s.lehrstoff).length;
  const fehlt = vergangen.length - mitStoff;
  const abw = gehalten.reduce((n, s) => n + s.abwesend, 0);
  const abwOffen = gehalten.reduce((n, s) => n + s.abwesend - s.entschuldigt, 0);
  const eintr = tage.reduce((n, t) => n + (t.eintraege ?? 0), 0);
  const hw = gehalten.filter(s => s.hausaufgaben).length;
  const prozent = vergangen.length ? Math.round(mitStoff / vergangen.length * 100) : 100;
  const k2 = { ...kontext, nachSpeichern: () => kontext.navigieren(location.hash) };
  const karten = alle.map(x => ({ datum: x.datum, von: x.von, bis: x.bis, beginn: x.beginn, ende: x.ende, status: x.status, fach: x.fach, fachName: x.fachName || x.fach,
    farbe: x.farbe, art: x.art ?? '', text: '', klassen: x.klassen.map(k => ({ k })), lehrer: x.lehrer.map(k => ({ k })), raeume: x.raeume.map(k => ({ k })), unterricht: x.unterricht }));

  const kachel = (zahl, text, { farbe = '', unter = '', f = '' } = {}) => `<${f ? `button type="button" data-f="${f}" aria-pressed="${f === filter}"` : 'div'} class="kbu-kachel ${farbe} ${f && f === filter ? 'aktiv' : ''}">
      <b>${zahl}</b><span>${e(text)}</span>${unter ? `<small>${unter}</small>` : ''}</${f ? 'button' : 'div'}>`;
  const zelleAbw = (s, kommend) => {
    if (s.zustand.art === 'entfall') return '';
    const offen = s.abwesend - s.entschuldigt;
    const teile = [];
    if (s.abwesend) teile.push(`<span class="kbu-abw" title="${s.abwesend} abwesend, davon ${s.entschuldigt} entschuldigt"><i class="bi bi-person-x"></i>${s.abwesend}${offen ? ` <small>(${offen} offen)</small>` : ''}</span>`);
    if (s.verspaetet) teile.push(`<span class="kbu-abw" title="${s.verspaetet} verspätet bzw. vorzeitig gegangen"><i class="bi bi-clock"></i>${s.verspaetet}</span>`);
    if (teile.length) return teile.join('');
    if (s.geprueft) return '<span class="kbu-still"><i class="bi bi-check2"></i> alle da</span>';
    return kommend ? '<span class="kbu-still">–</span>' : '<span class="kbu-still">nicht erfasst</span>';
  };
  const zeile = s => {
    const st = s.zustand;
    const i = alle.indexOf(s);
    const farbe = /^#[0-9a-f]{6}$/i.test(s.farbe || '') ? s.farbe : '';
    const wer = element.typ === 'klasse' ? s.lehrer : s.klassen;
    return `<div class="kbu-zeile ${st.art}" role="row" tabindex="0" data-s="${i}" aria-label="${e(`${s.von}. Stunde ${s.fach}`)}">
      <div class="kbu-std" role="cell"><b>${s.von === s.bis ? s.von : `${s.von}–${s.bis}`}</b><small>${e(s.beginn)}</small></div>
      <div class="kbu-fach" role="cell" style="${farbe ? `--fach:${farbe}` : ''}"><b>${e(s.fach)}</b><small>${e(wer.join(', '))}</small></div>
      <div class="kbu-stoff" role="cell">${st.art === 'entfall' ? '' : s.lehrstoff ? `<span class="kbu-text" title="${e(s.lehrstoff)}">${e(s.lehrstoff)}</span>`
        : st.kommend ? '<span class="kbu-still">–</span>' : '<span class="kbu-fehlt">Lehrstoff fehlt</span>'}</div>
      <div class="kbu-abwz" role="cell">${zelleAbw(s, st.kommend)}</div>
      <div class="kbu-eintr" role="cell">${s.eintraege ? `<span class="kbu-zahl" title="${s.eintraege} Klassenbucheinträge"><i class="bi bi-journal-text"></i>${s.eintraege}</span>` : '<span class="kbu-still">–</span>'}</div>
      <div class="kbu-hw" role="cell">${s.hausaufgaben ? `<span class="kbu-text" title="${e(s.hausaufgaben)}">${e(s.hausaufgaben)}</span>` : '<span class="kbu-still">–</span>'}</div>
      <div class="kbu-status" role="cell"><span class="kbu-st ${st.farbe}">${e(st.text)}</span><i class="bi bi-chevron-right kbu-pfeilchen" aria-hidden="true"></i></div></div>`;
  };
  const zeichnen = () => {
    const sichtbar = tage.map(t => ({ ...t, sicht: t.stunden.filter(s => passt(filter, s, s.zustand)) })).filter(t => t.sicht.length || filter === 'alle');
    seite.innerHTML = `<div class="kbu">
      <div class="kbu-kennzahlen">
        ${kachel(gehalten.length, gehalten.length === 1 ? 'Stunde' : 'Stunden', { unter: alle.length - gehalten.length ? `${alle.length - gehalten.length} entfallen` : modus === 'tag' ? 'an diesem Tag' : 'in dieser Woche' })}
        ${kachel(fehlt, 'Lehrstoff fehlt', { farbe: fehlt ? 'orange' : '', f: 'fehlt', unter: fehlt ? 'bitte nachtragen' : 'alles eingetragen' })}
        ${kachel(abw, abw === 1 ? 'Abwesenheit' : 'Abwesenheiten', { f: 'abwesend', unter: abw ? `${abwOffen} nicht entschuldigt` : 'keine' })}
        ${kachel(eintr, eintr === 1 ? 'Eintrag' : 'Einträge', { f: 'eintraege', unter: 'Klassenbucheinträge' })}
        ${kachel(hw, 'Hausaufgaben', { f: 'hausaufgaben', unter: 'aufgegeben' })}
        <div class="kbu-fortschritt" title="${mitStoff} von ${vergangen.length} gehaltenen Stunden mit Lehrstoff">
          <div class="kbu-fortschritt-kopf"><span>Lehrstoff eingetragen</span><b>${prozent} %</b></div>
          <div class="kbu-balken ${fehlt ? 'luecke' : ''}" role="progressbar" aria-valuemin="0" aria-valuemax="100" aria-valuenow="${prozent}" aria-label="Lehrstoff eingetragen"><span style="width:${prozent}%"></span></div>
          <small>${mitStoff} von ${vergangen.length} ${vergangen.length === 1 ? 'Stunde' : 'Stunden'}${fehlt ? ` · ${fehlt} offen` : ''}</small>
        </div>
      </div>
      <div class="kbu-pillen" role="tablist" aria-label="Filter">${FILTER.map(([f, t]) => `<button type="button" role="tab" aria-selected="${f === filter}" class="kbu-pille ${f === filter ? 'an' : ''}" data-f="${f}">${e(t)}${f === 'fehlt' && fehlt ? ` <span class="kbu-pille-zahl">${fehlt}</span>` : ''}</button>`).join('')}</div>
      ${!tage.length ? `<div class="kbu-leer">${leer(modus === 'tag' ? 'An diesem Tag gibt es keinen Unterricht.' : 'In dieser Woche gibt es keinen Unterricht.', 'calendar-x')}</div>`
        : !sichtbar.length ? `<div class="kbu-leer">${leer(filter === 'fehlt' ? 'Kein Lehrstoff fehlt – alles eingetragen.' : 'Keine passenden Stunden im Zeitraum.', filter === 'fehlt' ? 'check2-circle' : 'funnel')}</div>`
        : sichtbar.map(t => {
          const tf = t.stunden.filter(s => s.zustand.art === 'fehlt').length;
          return `<section class="kbu-tag ${t.datum === heute ? 'heute' : ''}" aria-label="${e(`${wtVon(t.datum)}, ${tt(t.datum)}`)}">
          <header class="kbu-tagkopf">
            <div class="kbu-datum"><b>${datumVon(t.datum).getDate()}</b><small>${MONATE_KURZ[datumVon(t.datum).getMonth()]}</small></div>
            <div class="kbu-tagtitel"><h2>${e(wtVon(t.datum))}${t.datum === heute ? ' <span class="kbu-heute-text">Heute</span>' : ''}</h2><span>${tt(t.datum)}${t.datum.slice(0, 4)} · ${t.stunden.length} ${t.stunden.length === 1 ? 'Stunde' : 'Stunden'}</span></div>
            <div class="kbu-tagchips">${tf ? `<span class="kbu-tagzahl warn">${tf} ohne Lehrstoff</span>` : ''}${t.eintraege ? `<span class="kbu-tagzahl"><i class="bi bi-journal-text"></i> ${t.eintraege} ${t.eintraege === 1 ? 'Eintrag' : 'Einträge'}</span>` : ''}</div>
          </header>
          <div class="kbu-tabelle" role="table">
            <div class="kbu-kopfzeile" role="row"><span role="columnheader">Std.</span><span role="columnheader">Fach</span><span role="columnheader">Lehrstoff</span><span role="columnheader">Abwesenheiten</span><span role="columnheader">Einträge</span><span role="columnheader">Hausaufgaben</span><span role="columnheader">Status</span></div>
            ${t.sicht.map(zeile).join('')}
          </div></section>`;
        }).join('')}</div>`;
    seite.querySelectorAll('[data-f]').forEach(b => b.onclick = () => {
      filter = b.dataset.f === filter && b.classList.contains('kbu-kachel') ? 'alle' : b.dataset.f;
      try { sessionStorage.setItem('untplan:kb-filter', filter); } catch { /* privat */ }
      zeichnen();
    });
    const oeffnen = z => karteDialog(karten[Number(z.dataset.s)], { ...k2, liste: karten });
    seite.querySelectorAll('[data-s]').forEach(z => {
      z.onclick = () => oeffnen(z);
      z.onkeydown = ev => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); oeffnen(z); } };
    });
  };
  zeichnen();
}

// ------------------------------------------------------------------ Wochenansicht einer Klasse: Raster Tage × Stunden

const WT_KURZ = ['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'];

/** Überlappende Stunden eines Tages zu Blöcken zusammenfassen und in Spuren nebeneinander legen (z. B. KR 2.–3. neben ER 2. und ER 3.). */
function bloecke(stunden) {
  const sortiert = stunden.slice().sort((a, b) => a.von - b.von || b.bis - a.bis);
  const liste = [];
  for (const s of sortiert) {
    const b = liste[liste.length - 1];
    if (b && s.von <= b.bis) { b.stunden.push(s); b.bis = Math.max(b.bis, s.bis); } else liste.push({ von: s.von, bis: s.bis, stunden: [s] });
  }
  for (const b of liste) {
    const spuren = [];
    for (const s of b.stunden) {
      let i = spuren.findIndex(ende => ende < s.von);
      if (i < 0) { i = spuren.length; spuren.push(0); }
      spuren[i] = s.bis;
      s.spur = i + 1;
    }
    b.spuren = spuren.length;
  }
  return liste;
}

/**
 * Klassenbuch einer Klasse für eine Woche als ruhiges Raster: Zeilen = Unterrichtsstunden, Spalten = Tage. Jede Stunde zeigt Fach,
 * Lehrkraft, Lehrstoff (bzw. „Lehrstoff fehlt“), Abwesenheiten, Klassenbucheinträge und Hausaufgaben; Klick öffnet die Stundendetails.
 * Darunter die Klassenbucheinträge (Vermerke) der Woche.
 */
async function wochenraster(inhalt, kontext, klasse, datum, ziel, k2, neuLaden) {
  const w = await holen(`/api/app/klassenbuch/woche?klasse=${encodeURIComponent(klasse)}&datum=${datum}`);
  const heute = heuteIso();
  const jetzt = jetztHhmm();
  const tage = w.tage.filter(t => t.stunden.length || t.frei || !['Sa', 'So'].includes(WT_KURZ[datumVon(t.datum).getDay()]));
  const genutzt = tage.flatMap(t => t.stunden.map(s => s.bis));
  const letzte = Math.max(0, ...genutzt, ...w.raster.filter(r => r.nr <= 6).map(r => r.nr));
  const raster = w.raster.filter(r => r.nr <= letzte);
  const zeileVon = nr => Math.max(0, raster.findIndex(r => r.nr === nr)) + 2;
  const karten = [];
  // Zahlen der Woche (dezent über dem Raster)
  const vergangen = s => s.status !== 'entfall' && (s.datum < heute || (s.datum === heute && (s.ende || '99:99') <= jetzt));
  const alle = tage.flatMap(t => t.stunden.map(s => Object.assign(s, { datum: t.datum })));
  const gehalten = alle.filter(vergangen);
  const ohneStoff = gehalten.filter(s => !s.thema).length;
  const ohneKontrolle = gehalten.filter(s => s.geprueft == null).length;
  const fehlend = alle.reduce((n, s) => n + (s.fehlend?.length ?? 0), 0);
  const eintraege = tage.reduce((n, t) => n + t.vermerke.length, 0);

  const karte = s => {
    const i = karten.length;
    karten.push({ datum: s.datum, von: s.von, bis: s.bis, beginn: s.beginn, ende: s.ende, status: s.status, fach: s.fach, fachName: s.fachName, farbe: s.farbe, art: s.art, text: '',
      klassen: [{ k: klasse }], lehrer: s.lehrer.map(k => ({ k })), raeume: s.raeume.map(k => ({ k })), unterricht: s.unterricht });
    const entfall = s.status === 'entfall';
    const kommend = !entfall && !vergangen(s);
    const farbe = /^#[0-9a-f]{6}$/i.test(s.farbe || '') ? s.farbe : '';
    const hw = (s.hausaufgaben?.length ? s.hausaufgaben : [s.hausaufgabe]).filter(Boolean);
    const zusatz = [];
    if (!entfall) {
      if (s.fehlend.length) zusatz.push(`<span class="kbw-info" title="${e(`fehlt: ${s.fehlend.join('; ')}`)}"><i class="bi bi-person-x"></i>${s.fehlend.length}</span>`);
      const spaet = s.verspaetet.length + (s.vorzeitig?.length ?? 0);
      if (spaet) zusatz.push(`<span class="kbw-info" title="${e([...s.verspaetet, ...(s.vorzeitig ?? [])].join('; '))}"><i class="bi bi-clock"></i>${spaet}</span>`);
      if (!s.fehlend.length && !spaet && s.geprueft != null) zusatz.push('<span class="kbw-info ok" title="Abwesenheiten kontrolliert – alle anwesend"><i class="bi bi-person-check"></i></span>');
      if (s.eintraege) zusatz.push(`<span class="kbw-info" title="${s.eintraege} ${s.eintraege === 1 ? 'Klassenbucheintrag' : 'Klassenbucheinträge'}"><i class="bi bi-journal-text"></i>${s.eintraege}</span>`);
      if (hw.length) zusatz.push(`<span class="kbw-info" title="${e(`Hausaufgabe: ${hw.join(' · ')}`)}"><i class="bi bi-house"></i></span>`);
    }
    const stoff = entfall ? `<span class="kbw-entfall">${e(s.art || 'Entfall')}</span>`
      : s.thema ? `<span class="kbw-stoff" title="${e(s.thema)}">${e(s.thema)}</span>`
      : kommend ? '' : '<span class="kbw-fehlt">Lehrstoff fehlt</span>';
    const offenKontrolle = !entfall && !kommend && s.geprueft == null;
    return `<button type="button" class="kbw-stunde ${entfall ? 'entfall' : ''} ${kommend ? 'kommend' : ''} ${!entfall && !kommend && !s.thema ? 'luecke' : ''}" data-k="${i}"
        style="grid-row:${s.von - s.blockVon + 1} / span ${s.bis - s.von + 1};grid-column:${s.spur};${farbe ? `--fach:${farbe}` : ''}"
        aria-label="${e(`${s.von === s.bis ? s.von : `${s.von}.–${s.bis}`}. Stunde ${s.fach}, ${s.lehrer.join(', ')}${entfall ? ', entfällt' : ''}`)}">
      <span class="kbw-kopf"><b>${e(s.fach)}</b><span>${e(s.lehrer.join(', '))}</span>${offenKontrolle ? '<i class="kbw-punkt" title="Abwesenheiten nicht kontrolliert"></i>' : ''}</span>
      ${stoff}
      ${zusatz.length ? `<span class="kbw-zusatz">${zusatz.join('')}</span>` : ''}
    </button>`;
  };

  const spalten = tage.map((t, x) => {
    const spalte = x + 2;
    const kopf = `<div class="kbw-tag ${t.datum === heute ? 'ist-heute' : ''}" style="grid-column:${spalte}">
        <span><b>${WT_KURZ[datumVon(t.datum).getDay()]}</b> ${tt(t.datum)}</span>
        ${!t.frei ? `<button type="button" class="kbw-plus" data-vermerk="${t.datum}" title="Klassenbucheintrag am ${tt(t.datum)}" aria-label="Klassenbucheintrag am ${tt(t.datum)}"><i class="bi bi-plus-lg"></i></button>` : ''}</div>`;
    if (t.frei) return `${kopf}<div class="kbw-frei" style="grid-column:${spalte};grid-row:2 / span ${Math.max(1, raster.length)}"><i class="bi bi-lock"></i>${e(t.freiGrund || 'unterrichtsfrei')}</div>`;
    const leer = raster.filter(r => !t.stunden.some(s => r.nr >= s.von && r.nr <= s.bis))
      .map(r => `<div class="kbw-leer ${t.datum === heute ? 'ist-heute' : ''}" style="grid-column:${spalte};grid-row:${zeileVon(r.nr)}"></div>`).join('');
    return kopf + leer + bloecke(t.stunden.filter(s => s.von <= letzte)).map(b => {
      b.stunden.forEach(s => { s.blockVon = b.von; });
      return `<div class="kbw-block ${t.datum === heute ? 'ist-heute' : ''}" style="grid-column:${spalte};grid-row:${zeileVon(b.von)} / span ${b.bis - b.von + 1};grid-template-columns:repeat(${b.spuren}, minmax(0, 1fr));grid-template-rows:repeat(${b.bis - b.von + 1}, minmax(0, 1fr))">
        ${b.stunden.map(karte).join('')}</div>`;
    }).join('');
  }).join('');

  const vermerke = tage.flatMap(t => t.vermerke.map(v => ({ ...v, datum: t.datum })));
  inhalt.innerHTML = `<div class="kbw">
    <div class="kbw-leiste">
      <div class="kbw-woche">
        <a class="kbu-pfeil" href="${ziel(klasse, 'woche', plusTage(w.von, -7))}" title="Vorherige Woche" aria-label="Vorherige Woche"><i class="bi bi-chevron-left"></i></a>
        <span class="kbw-zeitraum"><b>KW ${kwVon(w.von)}</b> ${tt(w.von)} – ${tt(w.bis)}${w.bis.slice(0, 4)}</span>
        <a class="kbu-pfeil" href="${ziel(klasse, 'woche', plusTage(w.von, 7))}" title="Nächste Woche" aria-label="Nächste Woche"><i class="bi bi-chevron-right"></i></a>
        <a class="kbw-heute ${heute >= w.von && heute <= w.bis ? 'aktiv' : ''}" href="${ziel(klasse, 'woche', heute)}">Heute</a>
      </div>
      <div class="kbw-zahlen">
        <span><b>${gehalten.length}</b> gehalten</span>
        <span class="${ohneStoff ? 'warn' : ''}"><b>${ohneStoff}</b> ohne Lehrstoff</span>
        <span><b>${ohneKontrolle}</b> nicht kontrolliert</span>
        <span><b>${fehlend}</b> Abwesenheiten</span>
        <span><b>${eintraege}</b> ${eintraege === 1 ? 'Eintrag' : 'Einträge'}</span>
        <span class="kbw-schueler">${w.schuelerAnzahl ? `${w.schuelerAnzahl} Schüler*innen` : 'keine Schülerliste übertragen'}</span>
      </div>
    </div>
    ${!raster.length || !tage.length ? `<div class="s-leer"><span class="s-leer-symbol"><i class="bi bi-calendar-x"></i></span><b>Kein Unterricht</b><p>In dieser Woche gibt es für ${e(klasse)} keinen Unterricht.</p></div>` : `
    <div class="kbw-rahmen"><div class="kbw-raster" style="--tage:${tage.length};grid-template-columns:64px repeat(${tage.length}, minmax(150px, 1fr));grid-template-rows:44px repeat(${raster.length}, minmax(92px, auto))">
      <div class="kbw-ecke"></div>
      ${raster.map(r => `<div class="kbw-nr" style="grid-row:${zeileVon(r.nr)}"><b>${r.nr}</b><small>${e(r.beginn)}</small></div>`).join('')}
      ${spalten}
    </div></div>
    <div class="kbw-legende"><span><span class="kbw-fehlt">Lehrstoff fehlt</span></span><span><i class="kbw-punkt"></i> Abwesenheiten nicht kontrolliert</span>
      <span><i class="bi bi-person-x"></i> fehlt</span><span><i class="bi bi-clock"></i> verspätet</span><span><i class="bi bi-journal-text"></i> Einträge</span><span><i class="bi bi-house"></i> Hausaufgabe</span></div>`}
    <section class="kbw-vermerke"><h2>Klassenbucheinträge der Woche</h2>
      ${vermerke.length ? `<ul>${vermerke.map(v => `<li><span class="kbw-v-datum">${WT_KURZ[datumVon(v.datum).getDay()]} ${tt(v.datum)}${v.zeit ? ` · ${e(v.zeit)}` : ''}</span>
          <span class="kbw-v-art">${e(v.art)}</span><span class="kbw-v-text">${v.schueler ? `<b>${e(v.schueler)}:</b> ` : ''}${e(v.text)}</span><span class="kbw-v-lehrer">${e(v.lehrer)}</span></li>`).join('')}</ul>`
        : '<p class="kbw-v-leer">Keine Einträge in dieser Woche.</p>'}
    </section></div>`;
  inhalt.querySelectorAll('[data-k]').forEach(b => b.onclick = () => karteDialog(karten[Number(b.dataset.k)], { ...k2, liste: karten }));
  inhalt.querySelectorAll('[data-vermerk]').forEach(b => b.onclick = () => vermerkDialog(klasse, b.dataset.vermerk, neuLaden));
}
