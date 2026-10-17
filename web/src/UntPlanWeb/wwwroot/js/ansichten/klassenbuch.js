// Klassenbuch: Wochenübersicht mit Anwesenheit, Fehlzeitenverwaltung, Vermerke, Inhalte/Hausaufgaben; Hausaufgaben für Klassenkonten.
import { e, icon, api, holen, senden, toast, alarm, datumKurz, datumLang, heuteIso, plusTage, leer, lade, infoHinweis, suchfeldMitFilter, suchfeldMitFilterBinden } from '../ui.js';
import { kbDialog as dialog, kbBestaetigen as bestaetigen } from '../kbdialog.js';
import { karteDialog } from '../plangitter.js';
import { elementWahl } from '../elementwahl.js';
import { kalenderOeffnen } from '../kalender.js';
import { detailFelder, detailWerte, NACHWEISE, abwZeitraum } from './abwesenheiten.js';
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
  const klasse = ich.kollegium ? (klasseParam && klasseParam !== '-' ? klasseParam : (reiterParam === 'fehlzeiten' && el.klassen.some(k => k.kurz === abwZeitraum()?.klasse && (ich.admin || ich.rolle === 'leitung' || (k.leitung ?? []).some(l => l.toLowerCase() === (ich.element ?? '').toLowerCase()))) ? abwZeitraum().klasse : null) ?? eigene ?? el.klassen[0]?.kurz) : ich.element;

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
  // Fehlzeiten und Abwesenheiten teilen sich Zeitraum und Klasse (gleiche Daten, gleiche Auswahl in beiden Bereichen)
  const gemeinsam = reiterParam === 'fehlzeiten' ? abwZeitraum() : null;
  const datum = /^\d{4}-\d{2}-\d{2}$/.test(datumParam ?? '') ? datumParam : gemeinsam?.bis ?? heuteIso();
  const ziel = (k, r, d = datum) => `#/klassenbuch/${encodeURIComponent(k ?? '')}/${r}/${d}`;
  // ohne Reiter-Leiste: Wochenübersicht, Fehlzeiten und Klassenbucheinträge erreicht man über das Klassenbuch-Menü
  seite.innerHTML = `
    <div class="u-filter mb-2"><label class="u-feld"><span>Klasse</span><span data-wahl></span></label></div>
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
    const von = /^\d{4}-\d{2}-\d{2}$/.test(vonParam ?? '') ? vonParam : gemeinsam?.von && gemeinsam.von <= datum ? gemeinsam.von : plusTage(datum, -90);
    abwZeitraum({ von, bis: datum, klasse });
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
    // gemeinsames Suchfeld mit Filter (Name, Status) – Stand bleibt beim Auf-/Zuklappen erhalten
    const fzSuche = { text: '', status: '' };
    const fzFiltern = () => inhalt.querySelectorAll('tr.fz-schueler').forEach(tr => {
      const x = zeilen.find(z => z.id === Number(tr.dataset.s));
      const aus = (fzSuche.text && !`${x.name} ${x.vorname}`.toLowerCase().includes(fzSuche.text)) || (fzSuche.status && !x[fzSuche.status]);
      tr.hidden = !!aus;
      if (tr.nextElementSibling?.classList.contains('fz-stunde')) tr.nextElementSibling.hidden = !!aus;
    });
    const zeichnen = () => {
      // kompakter Kopf: Klasse, Von, Bis, (i)-Hinweis und Kennzahlen in einer Zeile, rechts der Knopf
      inhalt.innerHTML = `<div class="nl-leiste u-kopfteil fz-kopfzeile">
          <span class="nl-pille" data-klwahl><span class="me-1 text-body-secondary">Klasse</span></span>
          <label class="nl-pille"><span class="me-1 text-body-secondary">Von</span><input type="date" data-von value="${von}"></label>
          <label class="nl-pille"><span class="me-1 text-body-secondary">Bis</span><input type="date" data-bis value="${datum}"></label>
          ${suchfeldMitFilter({ platzhalter: 'Schüler*in suchen', label: 'Schüler*in suchen', wert: fzSuche.text, filter: [
            { name: 'status', label: 'Status', alle: 'Alle mit Fehlzeiten', wert: fzSuche.status, optionen: [['offen', 'mit offenen Stunden'], ['unentschuldigt', 'mit nicht entschuldigten']] }] })}
          ${infoHinweis(`<b>Fehlstunden</b> entstehen aus einer Abwesenheit und dem Stundenplan: gezählt werden die Unterrichtsstunden, in denen die Schülerin bzw. der Schüler hätte anwesend sein müssen (z. B. ganzer Mittwoch abwesend, laut Plan 4 Stunden → 4 Fehlstunden). Den Zeitraum selbst erfassen Sie unter <a href="#/abwesenheiten/schueler/${von}/${datum}/${encodeURIComponent(klasse)}">Abwesenheiten</a> – beide Bereiche zeigen dieselben Daten.`)}
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
      suchfeldMitFilterBinden(inhalt.querySelector('.fz-kopfzeile'), {
        beiSuche: t => { fzSuche.text = t.toLowerCase(); fzFiltern(); },
        beiFilter: (_, w) => { fzSuche.status = w; fzFiltern(); },
      });
      fzFiltern();
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

// ------------------------------------------------------------------ Übersicht: Liste aller Unterrichte des Schuljahres

const MONATE = ['Januar', 'Februar', 'März', 'April', 'Mai', 'Juni', 'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember'];
const WT = ['Montag', 'Dienstag', 'Mittwoch', 'Donnerstag', 'Freitag', 'Samstag', 'Sonntag'];
const datumVon = iso => new Date(`${iso}T12:00`);
const wtVon = iso => WT[(datumVon(iso).getDay() + 6) % 7];
const tt = iso => { const d = datumVon(iso); return `${String(d.getDate()).padStart(2, '0')}.${String(d.getMonth() + 1).padStart(2, '0')}.`; };
const kwVon = iso => { const d = datumVon(iso); d.setDate(d.getDate() + 3 - ((d.getDay() + 6) % 7)); const j = new Date(d.getFullYear(), 0, 4); return 1 + Math.round(((d - j) / 864e5 - 3 + ((j.getDay() + 6) % 7)) / 7); };
const montagVon = iso => plusTage(iso, -((datumVon(iso).getDay() + 6) % 7));
const jetztHhmm = () => { const d = new Date(); return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`; };
const tagText = iso => { const d = datumVon(iso); return `${d.getDate()}. ${MONATE[d.getMonth()]} ${d.getFullYear()}`; };
const BLOCK = 21; // Tage je Nachladen

/** Kriterien der Übersicht aus der Adresse: „k:7a,l:LAN,f:D“ (alt: „7a“ bzw. „@LAN“). */
export function kriterienAus(code) {
  if (!code || code === '-') return [];
  if (code.startsWith('@')) return [{ typ: 'lehrer', kurz: code.slice(1) }];
  if (!/^[klf]:/.test(code)) return [{ typ: 'klasse', kurz: code }];
  const TYP = { k: 'klasse', l: 'lehrer', f: 'fach' };
  return code.split(',').map(t => ({ typ: TYP[t[0]], kurz: t.slice(2) })).filter(x => x.typ && x.kurz);
}
export const kriterienCode = liste => liste.length ? liste.map(x => `${x.typ[0]}:${x.kurz}`).join(',') : '-';

/** Abwesenheiten einer Stunde als Tags: Anzahl offen bzw. nicht entschuldigt | gesamt, Verspätungen, „Keine Abwesenheiten“, „Abwesenheiten prüfen“. */
export function abwesenheitTags(s, kommend) {
  if (s.status === 'entfall' || kommend) return '';
  const teile = [];
  if (s.abwesend) {
    const unent = s.unentschuldigt ?? 0;
    const offen = s.abwesend - s.entschuldigt - unent;
    const [farbe, text] = unent ? ['rot', 'Nicht entschuldigt'] : offen ? ['gelb', 'Offen'] : ['gruen', 'Entschuldigt'];
    teile.push(`<span class="kbl-tag zahl ${farbe}" title="${s.abwesend} abwesend: ${s.entschuldigt} entschuldigt, ${offen} offen, ${unent} nicht entschuldigt"><b class="kbl-offen">${offen + unent}</b><i class="kbl-trenn"></i><b>${s.abwesend}</b> ${text}</span>`);
  }
  if (s.verspaetet) teile.push(`<span class="kbl-tag" title="${s.verspaetet} verspätet bzw. vorzeitig gegangen"><i class="bi bi-clock"></i>${s.verspaetet} verspätet</span>`);
  if (teile.length) return teile.join('');
  return s.geprueft ? '<span class="kbl-tag gruen"><i class="bi bi-check2"></i>Keine Abwesenheiten</span>' : '<span class="kbl-tag pruefen">Abwesenheiten prüfen</span>';
}

/**
 * Klassenbuch → Übersicht wie im Original: alle Unterrichte des aktuellen Schuljahres als Liste, je Tag eine Tabelle
 * h | Fach | Lehrkräfte | Klassen | Lehrstoff | Abwesenheiten (Tags) | Hausaufgaben. Navigation durch Scrollen (rauf lädt vergangene, runter
 * zukünftige Unterrichte bis zum Schuljahresende), Datumspille je Tag mit Heute/Schuljahresbeginn/Schuljahresende/Kalender, Filter
 * „Lehrstoff fehlt“ und „Abwesenheiten nicht kontrolliert“. Lehrkräfte wählen oben ihre Unterrichte, Klassenleitungen zusätzlich eine
 * Lehrkraft ihrer Klassen; Administration und Schulleitung suchen (Klasse/Lehrkraft nötig, Fach optional). Zeile → Stundendetails.
 * Adresse: #/klassenbuch/<Kriterien | ->/uebersicht/<Datum>/<Unterricht | ->
 */
async function uebersicht(seite, kontext, kritParam, datumParam, unterrichtParam) {
  const ich = kontext.ich;
  const el = await kontext.elemente();
  const heute = heuteIso();
  const sj = kontext.sitzung.schuljahr ?? {};
  const sjVon = sj.von || plusTage(heute, -300), sjBis = sj.bis || plusTage(heute, 300);
  const begrenzt = iso => iso < sjVon ? sjVon : iso > sjBis ? sjBis : iso;
  const ziel_datum = begrenzt(/^\d{4}-\d{2}-\d{2}$/.test(datumParam ?? '') ? datumParam : heute);
  const unterricht = /^\d+$/.test(unterrichtParam ?? '') ? Number(unterrichtParam) : null;
  const suchModus = !!ich.admin || !ich.element;
  let kriterien = kriterienAus(kritParam);
  const ziel = ({ k = kriterien, d = ziel_datum, u = unterricht } = {}) => `#/klassenbuch/${encodeURIComponent(kriterienCode(k))}/uebersicht/${d}/${u ?? '-'}`;
  let filter = new Set();
  try { filter = new Set(JSON.parse(sessionStorage.getItem('untplan:kbl-filter') || '[]')); } catch { /* privat */ }
  kontext.titel('');

  // ---------------------------------------------------------------- Auswahl: eigene Unterrichte, Lehrkraft (Klassenleitung) bzw. Suche
  const eigeneKlassen = suchModus ? [] : el.klassen.filter(k => (k.leitung ?? []).some(l => l.toLowerCase() === ich.element.toLowerCase())).map(k => k.kurz);
  let lehrkraft = suchModus ? '' : kriterien.find(x => x.typ === 'lehrer' && eigeneKlassen.length)?.kurz ?? ich.element;
  if (!suchModus && lehrkraft.toLowerCase() !== ich.element.toLowerCase() && !eigeneKlassen.length) lehrkraft = ich.element;
  const fremd = !suchModus && lehrkraft.toLowerCase() !== ich.element.toLowerCase();
  let unterrichte = [], kollegen = [];
  if (!suchModus) {
    const [eigene, ...klassenU] = await Promise.all([
      holen(`/api/app/unterricht?lehrer=${encodeURIComponent(lehrkraft)}`).catch(() => []),
      ...eigeneKlassen.map(k => holen(`/api/app/unterricht?klasse=${encodeURIComponent(k)}`).catch(() => [])),
    ]);
    const inKlassen = new Set(klassenU.flat().map(u => u.id));
    unterrichte = eigene.filter(u => u.klassen.length && (!fremd || inKlassen.has(u.id)));
    kollegen = [...new Set(klassenU.flat().flatMap(u => u.lehrer))].filter(l => l.toLowerCase() !== ich.element.toLowerCase()).sort((a, b) => a.localeCompare(b, 'de'));
  }
  const lehrerName = kurz => el.lehrer.find(l => l.kurz === kurz)?.name || kurz;
  const uText = u => `${u.klassen.join(', ')}<span class="kbl-trennstrich">|</span>${e(u.fach)}`;
  const gewaehlterU = unterrichte.find(u => u.id === unterricht);
  const filterKnoepfe = `<div class="kbl-filter" role="group" aria-label="Filter">
      <button type="button" class="kbl-fknopf ${filter.has('lehrstoff') ? 'an' : ''}" data-f="lehrstoff" aria-pressed="${filter.has('lehrstoff')}">Lehrstoff fehlt</button>
      <button type="button" class="kbl-fknopf ${filter.has('kontrolle') ? 'an' : ''}" data-f="kontrolle" aria-pressed="${filter.has('kontrolle')}">Abwesenheiten nicht kontrolliert</button></div>`;
  const w = kontext.werkzeug(suchModus ? `<div class="kbl-kopf">
      <div class="os-suche kbl-suche" data-suchfeld>${icon('suchen')}
        ${kriterien.map((c, i) => `<span class="os-chip">${e(c.typ === 'fach' ? c.kurz : c.kurz)}<button type="button" data-chip-weg="${i}" aria-label="Filter ${e(c.kurz)} entfernen"><i class="bi bi-x"></i></button></span>`).join('')}
        <input type="search" data-suche autocomplete="off" placeholder="Suche" aria-label="Klasse, Lehrkraft oder Fach suchen" aria-expanded="false" aria-controls="kbl-vorschlaege">
        ${kriterien.length ? '<button type="button" class="os-leeren" data-leeren title="Alle Kriterien entfernen" aria-label="Alle Kriterien entfernen"><i class="bi bi-x-circle-fill"></i></button>' : ''}
        <div class="os-vorschlaege" id="kbl-vorschlaege" role="listbox" hidden></div></div>
      ${filterKnoepfe}</div>`
    : `<div class="kbl-kopf">
      <div class="kbl-wahl"><button type="button" class="kbl-titelwahl" data-menue="unterricht" aria-haspopup="listbox" aria-expanded="false">${gewaehlterU ? uText(gewaehlterU) : 'Alle Unterrichte'}<i class="bi bi-chevron-down"></i></button></div>
      <div class="kbl-zeile2">${eigeneKlassen.length ? `<button type="button" class="kbl-lehrkraft" data-menue="lehrer" aria-haspopup="listbox" aria-expanded="false" title="Unterrichte einer anderen Lehrkraft in ${eigeneKlassen.length === 1 ? 'Ihrer Klasse' : 'Ihren Klassen'}">${e(lehrerName(lehrkraft))}<i class="bi bi-chevron-down"></i></button>` : ''}${filterKnoepfe}</div></div>`);

  // Auswahllisten (Unterrichte, Lehrkräfte) als kleines Menü unter dem Knopf
  const menueOeffnen = (knopf, eintraege, beiWahl) => {
    document.querySelector('.kbl-menue')?.remove();
    const m = document.createElement('div');
    m.className = 'kbl-menue';
    m.setAttribute('role', 'listbox');
    m.innerHTML = eintraege.map((x, i) => `<button type="button" role="option" data-i="${i}" class="${x.an ? 'an' : ''}" aria-selected="${!!x.an}">${x.html}</button>`).join('');
    knopf.parentElement.append(m);
    knopf.setAttribute('aria-expanded', 'true');
    const zu = () => { m.remove(); knopf.setAttribute('aria-expanded', 'false'); document.removeEventListener('mousedown', aussen); document.removeEventListener('keydown', taste); };
    const aussen = ev => { if (!m.contains(ev.target) && ev.target !== knopf && !knopf.contains(ev.target)) zu(); };
    const taste = ev => { if (ev.key === 'Escape') { zu(); knopf.focus(); } };
    document.addEventListener('mousedown', aussen);
    document.addEventListener('keydown', taste);
    m.querySelectorAll('[data-i]').forEach(b => b.onclick = () => { zu(); beiWahl(eintraege[Number(b.dataset.i)]); });
    (m.querySelector('.an') ?? m.querySelector('button'))?.focus();
  };
  w.querySelector('[data-menue="unterricht"]')?.addEventListener('click', ev => {
    if (ev.currentTarget.getAttribute('aria-expanded') === 'true') return;
    menueOeffnen(ev.currentTarget, [{ html: 'Alle Unterrichte', id: null, an: !unterricht }, ...unterrichte.map(u => ({ html: uText(u), id: u.id, an: u.id === unterricht }))],
      x => kontext.navigieren(ziel({ u: x.id })));
  });
  w.querySelector('[data-menue="lehrer"]')?.addEventListener('click', ev => {
    if (ev.currentTarget.getAttribute('aria-expanded') === 'true') return;
    menueOeffnen(ev.currentTarget, [{ html: `${e(lehrerName(ich.element))} <small>eigene Unterrichte</small>`, kurz: ich.element, an: !fremd },
      ...kollegen.map(l => ({ html: `${e(lehrerName(l))}${lehrerName(l) !== l ? ` <small>${e(l)}</small>` : ''}`, kurz: l, an: l === lehrkraft }))],
      x => kontext.navigieren(ziel({ k: x.kurz === ich.element ? [] : [{ typ: 'lehrer', kurz: x.kurz }], u: null })));
  });

  // Abfrage je nach Modus
  const abfrage = () => {
    const q = new URLSearchParams();
    if (suchModus) {
      const k = kriterien.filter(x => x.typ === 'klasse').map(x => x.kurz), l = kriterien.filter(x => x.typ === 'lehrer').map(x => x.kurz), f = kriterien.filter(x => x.typ === 'fach').map(x => x.kurz);
      if (!k.length && !l.length) return null;
      if (k.length) q.set('klasse', k.join(','));
      if (l.length) q.set('lehrer', l.join(','));
      if (f.length) q.set('fach', f.join(','));
    } else if (fremd) { q.set('klasse', eigeneKlassen.join(',')); q.set('lehrer', lehrkraft); }
    else q.set('lehrer', ich.element);
    if (unterricht) q.set('unterricht', unterricht);
    return q;
  };
  const basis = abfrage();
  if (suchModus) sucheBinden();
  if (!basis) {
    seite.innerHTML = `<div class="kbl-leerseite"><span class="kbl-leer-symbol"><i class="bi bi-search"></i></span>
      <p>${kriterien.length ? 'Nur ein Fach genügt nicht – bitte zusätzlich eine Klasse oder Lehrkraft angeben.' : 'Benutzen Sie die Suche, um das Ergebnis mit zumindest einer Lehrkraft und/oder Klasse einzuschränken.'}</p></div>`;
    filterBinden(() => {});
    return;
  }

  // ---------------------------------------------------------------- Laden (blockweise) und Zeichnen
  const tage = new Map();           // Datum → Tag mit Stunden
  let von = null, bis = null;       // geladener Bereich
  let ladend = false;
  const holeBereich = async (a, b) => {
    for (let x = a; x <= b; x = plusTage(x, 31)) {
      const y = plusTage(x, 30) < b ? plusTage(x, 30) : b;
      const q = new URLSearchParams(basis); q.set('von', x); q.set('bis', y);
      const r = await holen(`/api/app/klassenbuch/uebersicht?${q}`);
      for (const t of r.tage) tage.set(t.datum, t);
    }
  };
  const kommend = (s, datum) => s.status !== 'entfall' && (datum > heute || (datum === heute && (s.beginn || '99:99') > jetztHhmm()));
  const passt = (s, datum) => {
    if (!filter.size) return true;
    if (s.status === 'entfall' || kommend(s, datum)) return false;
    return (!filter.has('lehrstoff') || !s.lehrstoff) && (!filter.has('kontrolle') || !s.geprueft);
  };
  const karten = [];
  // eigene Unterrichte ohne Spalte „Lehrkräfte“ (wie im Original); bei fremder Lehrkraft bzw. Suche mit
  const mitLehrer = suchModus || fremd;
  seite.innerHTML = `<div class="kbl">
    <div class="kbl-rand" data-oben></div>
    <div data-liste>${lade()}</div>
    <div class="kbl-rand" data-unten></div>
    <input type="date" class="kbl-kalender" data-kalender min="${sjVon}" max="${sjBis}" tabindex="-1" aria-hidden="true"></div>`;
  const listeEl = seite.querySelector('[data-liste]');
  const zeichnen = () => {
    karten.length = 0;
    const sortiert = [...tage.values()].sort((a, b) => a.datum.localeCompare(b.datum));
    let letzteKw = null;
    const html = sortiert.map(t => {
      const sicht = t.stunden.filter(s => passt(s, t.datum));
      if (!sicht.length) return '';
      const kw = kwVon(t.datum);
      const kwNeu = kw !== letzteKw; letzteKw = kw;
      return `<section class="kbl-tagblock ${t.datum === heute ? 'ist-heute' : ''}" data-datum="${t.datum}">
        <div class="kbl-trenner"><span class="kbl-links"><span class="kbl-kw">${kwNeu ? `KW ${kw}` : ''}</span><span class="kbl-linie"></span></span>
          <button type="button" class="kbl-datum" data-datumwahl="${t.datum}" aria-haspopup="menu" aria-expanded="false">${e(wtVon(t.datum))}<span class="kbl-trennstrich">|</span>${e(tagText(t.datum))}${t.datum === heute ? ' <span class="kbl-heute">Heute</span>' : ''}<i class="bi bi-chevron-down"></i></button>
          <span class="kbl-linie"></span></div>
        <div class="kbl-tabelle ${mitLehrer ? '' : 'ohne-lehrer'}" role="table" aria-label="${e(`${wtVon(t.datum)}, ${tagText(t.datum)}`)}">
          <div class="kbl-kopfzeile" role="row"><span role="columnheader">h</span><span role="columnheader">Fach</span>${mitLehrer ? '<span role="columnheader">Lehrkräfte</span>' : ''}<span role="columnheader">Klassen</span>
            <span role="columnheader">Lehrstoff</span><span role="columnheader">Abwesenheiten</span><span role="columnheader">Hausaufgaben</span></div>
          ${sicht.map(s => {
            const i = karten.length;
            karten.push({ datum: t.datum, von: s.von, bis: s.bis, beginn: s.beginn, ende: s.ende, status: s.status, fach: s.fach, fachName: s.fachName || s.fach, farbe: s.farbe, art: s.art ?? '', text: '',
              klassen: s.klassen.map(k => ({ k })), lehrer: s.lehrer.map(k => ({ k })), raeume: s.raeume.map(k => ({ k })), unterricht: s.unterricht });
            const entfall = s.status === 'entfall', spaeter = kommend(s, t.datum);
            const farbe = /^#[0-9a-f]{6}$/i.test(s.farbe || '') ? s.farbe : '';
            return `<div class="kbl-zeile ${entfall ? 'entfall' : ''} ${spaeter ? 'kommend' : ''}" role="row" tabindex="0" data-k="${i}" aria-label="${e(`${s.von}. Stunde ${s.fach} ${s.klassen.join(', ')}`)}">
              <span role="cell" class="kbl-h">${s.von === s.bis ? s.von : `${s.von}–${s.bis}`}</span>
              <span role="cell" class="kbl-fach" style="${farbe ? `--fach:${farbe}` : ''}">${e(s.fach)}</span>
              ${mitLehrer ? `<span role="cell" class="kbl-lehrer">${e(s.lehrer.map(l => lehrerName(l)).join(', '))}</span>` : ''}
              <span role="cell" class="kbl-klassen">${e(s.klassen.join(', '))}</span>
              <span role="cell" class="kbl-stoff">${entfall ? `<span class="kbl-tag rot">${e(s.art || 'Entfall')}</span>` : s.lehrstoff ? `<span class="kbl-text" title="${e(s.lehrstoff)}">${e(s.lehrstoff)}</span>` : ''}</span>
              <span role="cell" class="kbl-abw">${abwesenheitTags(s, spaeter)}</span>
              <span role="cell" class="kbl-hw">${s.hausaufgaben ? `<span class="kbl-text" title="${e(s.hausaufgaben)}">${e(s.hausaufgaben)}</span>` : ''}</span></div>`;
          }).join('')}
        </div></section>`;
    }).join('');
    const anfang = von <= sjVon, ende = bis >= sjBis;
    listeEl.innerHTML = `${anfang ? `<div class="kbl-grenze"><i class="bi bi-flag"></i>Beginn des Schuljahres · ${tt(sjVon)}${sjVon.slice(0, 4)}</div>` : ''}
      ${html || `<div class="kbl-leerseite klein"><span class="kbl-leer-symbol"><i class="bi bi-${filter.size ? 'check2-circle' : 'calendar-x'}"></i></span><p>${filter.size ? 'Im geladenen Zeitraum gibt es keine passenden Unterrichte.' : 'Im geladenen Zeitraum gibt es keinen Unterricht.'}</p></div>`}
      ${ende ? `<div class="kbl-grenze"><i class="bi bi-flag-fill"></i>Ende des Schuljahres · ${tt(sjBis)}${sjBis.slice(0, 4)}</div>` : ''}`;
    listeEl.querySelectorAll('[data-k]').forEach(z => {
      const oeffnen = () => karteDialog(karten[Number(z.dataset.k)], { ...kontext, liste: karten, nachSpeichern: aktualisieren });
      z.onclick = oeffnen;
      z.onkeydown = ev => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); oeffnen(); } };
    });
    listeEl.querySelectorAll('[data-datumwahl]').forEach(b => b.onclick = () => datumMenue(b));
  };
  let letzterScroll = 0;
  const beimScrollen = () => { letzterScroll = performance.now(); if (!seite.isConnected) window.removeEventListener('scroll', beimScrollen); };
  window.addEventListener('scroll', beimScrollen, { passive: true });
  // Nachladen; Position bleibt beim Voranstellen erhalten
  const nachladen = async richtung => {
    if (ladend) return false;
    if (richtung < 0 ? von <= sjVon : bis >= sjBis) return false;
    ladend = true;
    try {
      const a = richtung < 0 ? begrenzt(plusTage(von, -BLOCK)) : plusTage(bis, 1);
      const b = richtung < 0 ? plusTage(von, -1) : begrenzt(plusTage(bis, BLOCK));
      await holeBereich(a, b);
      // erst zeichnen, wenn das (weiche) Scrollen zur Ruhe gekommen ist – sonst überschreibt die laufende Bewegung die Korrektur
      while (seite.isConnected && performance.now() - letzterScroll < 180) await new Promise(ok => setTimeout(ok, 60));
      if (!seite.isConnected) return false;
      const anker = ankerMerken();
      if (richtung < 0) von = a; else bis = b;
      zeichnen();
      ankerHerstellen(anker);
      return true;
    } catch (err) { toast(err.message, true); return false; }
    finally { ladend = false; }
  };
  // solange ein Rand sichtbar ist (z. B. viele leere Tage bei aktivem Filter), weiter laden
  const sichtbar = x => { const r = x.getBoundingClientRect(); return r.top < innerHeight + 300 && r.bottom > -300; };
  const pruefen = async () => {
    for (let n = 0; n < 15 && seite.isConnected; n++) {
      if (sichtbar(seite.querySelector('[data-unten]')) && await nachladen(1)) continue;
      if (sichtbar(seite.querySelector('[data-oben]')) && await nachladen(-1)) continue;
      break;
    }
  };
  // Lage beim Neuzeichnen halten: oberster sichtbarer Tag bleibt an derselben Stelle
  const ankerMerken = () => {
    const x = [...listeEl.querySelectorAll('[data-datum]')].find(t => t.getBoundingClientRect().bottom > 120);
    return x ? { datum: x.dataset.datum, top: x.getBoundingClientRect().top } : null;
  };
  const ankerHerstellen = anker => {
    const x = anker && (listeEl.querySelector(`[data-datum="${anker.datum}"]`) ?? [...listeEl.querySelectorAll('[data-datum]')].find(t => t.dataset.datum >= anker.datum));
    if (x) window.scrollTo({ top: window.scrollY + x.getBoundingClientRect().top - anker.top, behavior: 'instant' });
  };
  async function aktualisieren() {
    const anker = ankerMerken();
    try { tage.clear(); await holeBereich(von, bis); zeichnen(); ankerHerstellen(anker); } catch (err) { toast(err.message, true); }
  }
  const springen = d => {
    const ziel2 = listeEl.querySelector(`[data-datum="${d}"]`) ?? [...listeEl.querySelectorAll('[data-datum]')].find(x => x.dataset.datum >= d);
    if (ziel2) window.scrollTo({ top: ziel2.getBoundingClientRect().top + window.scrollY - (document.getElementById('werkzeug')?.getBoundingClientRect().bottom ?? 0) - 12, behavior: 'instant' });
  };
  // Datumspille: Heute, Schuljahresbeginn, Schuljahresende, Kalender öffnen
  const kalender = seite.querySelector('[data-kalender]');
  kalender.onchange = () => { if (kalender.value) kontext.navigieren(ziel({ d: begrenzt(kalender.value) })); };
  function datumMenue(knopf) {
    if (knopf.getAttribute('aria-expanded') === 'true') return;
    kalender.value = knopf.dataset.datumwahl;
    menueOeffnen(knopf, [{ html: 'Heute', d: heute }, { html: 'Schuljahresbeginn', d: sjVon }, { html: 'Schuljahresende', d: sjBis }, { html: 'Kalender öffnen', kal: true }], x => {
      if (x.kal) { setTimeout(() => kalenderOeffnen(knopf, { wert: knopf.dataset.datumwahl, min: sjVon, max: sjBis, fokusSetzen: true, beiWahl: d => { kalender.value = d; kalender.onchange(); } })); return; }
      const d = begrenzt(x.d);
      if (d >= von && d <= bis && listeEl.querySelector(`[data-datum="${d}"]`)) springen(d); else kontext.navigieren(ziel({ d }));
    });
  }
  filterBinden(() => { const anker = ankerMerken(); zeichnen(); ankerHerstellen(anker); pruefen(); });

  von = begrenzt(plusTage(montagVon(ziel_datum), -7));
  bis = begrenzt(plusTage(ziel_datum, BLOCK));
  try { await holeBereich(von, bis); } catch (err) { listeEl.innerHTML = alarm('danger', err.message); return; }
  if (!seite.isConnected) return;
  zeichnen();
  springen(ziel_datum);
  const beobachter = new IntersectionObserver(eintraege => { if (eintraege.some(x => x.isIntersecting)) pruefen(); }, { rootMargin: '300px 0px' });
  setTimeout(() => {
    if (!seite.isConnected) return;
    beobachter.observe(seite.querySelector('[data-oben]'));
    beobachter.observe(seite.querySelector('[data-unten]'));
    window.addEventListener('hashchange', () => beobachter.disconnect(), { once: true });
  }, 250);

  function filterBinden(neu) {
    w.querySelectorAll('[data-f]').forEach(b => b.onclick = () => {
      const f = b.dataset.f;
      if (filter.has(f)) filter.delete(f); else filter.add(f);
      b.classList.toggle('an', filter.has(f)); b.setAttribute('aria-pressed', String(filter.has(f)));
      try { sessionStorage.setItem('untplan:kbl-filter', JSON.stringify([...filter])); } catch { /* privat */ }
      neu();
    });
  }

  // Suchleiste (Administration, Schulleitung): Vorschläge zu Klassen, Fächern und Lehrkräften, mehrere Kriterien als Chips
  function sucheBinden() {
    const suche = w.querySelector('[data-suche]');
    const box = w.querySelector('#kbl-vorschlaege');
    let markiert = -1;
    const gruppen = () => {
      const t = suche.value.trim().toLowerCase();
      const hat = (typ, kurz) => kriterien.some(x => x.typ === typ && x.kurz === kurz);
      const p = (...x) => !t || x.some(y => (y ?? '').toLowerCase().includes(t));
      const g = [];
      const kl = el.klassen.filter(k => !hat('klasse', k.kurz) && p(k.kurz, k.name)).slice(0, t ? 12 : 6).map(k => ({ typ: 'klasse', kurz: k.kurz, info: k.name && k.name !== k.kurz ? k.name : '' }));
      if (kl.length) g.push(['Klassen', kl]);
      const le = el.lehrer.filter(l => !hat('lehrer', l.kurz) && p(l.kurz, l.name)).slice(0, t ? 12 : 6).map(l => ({ typ: 'lehrer', kurz: l.kurz, info: l.name }));
      if (le.length) g.push(['Lehrkräfte', le]);
      const fa = (el.faecher ?? []).filter(f => !hat('fach', f.kurz) && p(f.kurz, f.name)).slice(0, t ? 12 : 6).map(f => ({ typ: 'fach', kurz: f.kurz, info: f.name && f.name !== f.kurz ? f.name : '' }));
      if (fa.length) g.push(['Fächer', fa]);
      return g;
    };
    const liste = () => [...box.querySelectorAll('[data-v]')];
    const zeigen2 = () => {
      const g = gruppen();
      markiert = -1;
      box.innerHTML = g.map(([titel, x]) => `<div class="os-v-titel">${e(titel)}</div>${x.map(v => `<button type="button" role="option" data-v="${v.typ}:${e(v.kurz)}"><b>${e(v.kurz)}</b>${v.info ? `<small>${e(v.info)}</small>` : ''}</button>`).join('')}`).join('');
      box.hidden = !g.length;
      suche.setAttribute('aria-expanded', String(!box.hidden));
      liste().forEach(b => b.onmousedown = ev => { ev.preventDefault(); waehlen(b.dataset.v); });
    };
    const waehlen = v => { const [typ, ...r] = v.split(':'); kontext.navigieren(ziel({ k: [...kriterien, { typ, kurz: r.join(':') }], u: null })); };
    suche.onfocus = zeigen2;
    suche.oninput = zeigen2;
    suche.onblur = () => setTimeout(() => { box.hidden = true; suche.setAttribute('aria-expanded', 'false'); }, 120);
    suche.onkeydown = ev => {
      const l = liste();
      if (ev.key === 'ArrowDown' || ev.key === 'ArrowUp') {
        ev.preventDefault();
        if (box.hidden) zeigen2();
        markiert = Math.max(0, Math.min(l.length - 1, markiert + (ev.key === 'ArrowDown' ? 1 : -1)));
        l.forEach((b, i) => b.classList.toggle('aktiv', i === markiert));
        l[markiert]?.scrollIntoView({ block: 'nearest' });
      } else if (ev.key === 'Enter' && (markiert >= 0 || l.length === 1)) { ev.preventDefault(); waehlen(l[Math.max(0, markiert)].dataset.v); }
      else if (ev.key === 'Escape') box.hidden = true;
      else if (ev.key === 'Backspace' && !suche.value && kriterien.length) kontext.navigieren(ziel({ k: kriterien.slice(0, -1), u: null }));
    };
    w.querySelectorAll('[data-chip-weg]').forEach(b => b.onclick = () => kontext.navigieren(ziel({ k: kriterien.filter((_, i) => i !== Number(b.dataset.chipWeg)), u: null })));
    w.querySelector('[data-leeren]')?.addEventListener('click', () => kontext.navigieren(ziel({ k: [], u: null })));
    w.querySelector('[data-suchfeld]').addEventListener('click', ev => { if (!ev.target.closest('button')) suche.focus(); });
  }
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
