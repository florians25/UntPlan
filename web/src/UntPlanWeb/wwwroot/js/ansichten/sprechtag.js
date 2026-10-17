// Sprechtag (Elternsprechtag) im nl-design:
// - Liste der Sprechtage (#/sprechtag), Administration: anlegen und bearbeiten (#/sprechtag/neu, #/sprechtag/<id>/verwalten) mit Zeitfenster,
//   Termindauer, Pausen, Einladungs- und Anmeldezeitraum, beteiligten Lehrkräften und Räumen, Sichtbarkeit; Übersicht je Lehrkraft.
// - Lehrkraft (#/sprechtag/<id>, Administration: #/sprechtag/<id>/lehrer/<Kürzel>): Raum, „Gespräche wünschen“ (Einladungen an die Eltern
//   ausgewählter Schüler*innen per Mitteilung), „Meine Termine“ als Zeitraster (frei/gebucht/gesperrt) mit Sperren, manuellem Eintragen,
//   Verschieben, Absagen (einzelne Termine mit Grund bzw. die ganze Teilnahme) und Terminplan als PDF (S/W).
// - Administration: ganzen Sprechtag absagen (alle Beteiligten werden per Mitteilung benachrichtigt).
// - Erziehungsberechtigte: Lehrkräfte je Kind mit freien Zeitfenstern, Einladungen, buchen, Termin absagen (mit Grund), eigener Terminplan als PDF.
// - startkarte(): Hinweis „Sprechtag“ auf der Startseite (Heute).
// - Status „angekündigt“: freigegeben, Anmeldung beginnt noch – Familien sehen Datum, Zeit, Ort und Anmeldebeginn; Schüler*innen (Option) nur lesen.
// - Optionen: Zeitblöcke, Termindauer je Lehrkraft, Puffer, Höchstzahl je Familie, Warteliste, Online-Termine mit Link, Erinnerungen, weitere Tage.
import { e, icon, holen, senden, api, toast, dialog, bestaetigen, leer, alarm } from '../ui.js';

const BASIS = '/api/app/sprechtag';

function stileLaden() {
  if (document.querySelector('link[data-sprechtag-css]')) return;
  const l = document.createElement('link');
  l.rel = 'stylesheet';
  l.href = new URL('../../css/sprechtag.css', import.meta.url).href;
  l.dataset.sprechtagCss = '1';
  document.head.append(l);
}

const zeitDe = iso => {
  const m = String(iso || '').match(/^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/);
  return m ? `${m[3]}.${m[2]}.${m[1]} ${m[4]}:${m[5]}` : '';
};
const PHASEN = {
  vorbereitung: ['grau', 'nicht sichtbar'], einladung: ['lila', 'Einladungsphase'], angekuendigt: ['lila', 'angekündigt'], anmeldung: ['gruen', 'Anmeldung läuft'],
  geschlossen: ['gelb', 'Anmeldung geschlossen'], vorbei: ['grau', 'vorbei'], abgesagt: ['rot', 'abgesagt'],
};
/** Termine bzw. Teilnahme können bis zum Ende des Sprechtags abgesagt werden. */
const absagbar = t => !['vorbei', 'abgesagt'].includes(t.phase);

/** Absage mit optionalem Grund; liefert den Grund ('' = ohne) bzw. null bei Abbruch. */
function absageDialog(titel, text, knopf = 'Absagen') {
  return new Promise(ok => {
    let grund = null;
    const box = dialog({
      titel, inhalt: `<p>${e(text)}</p><label class="form-label" for="st-grund">Grund (optional)</label>
        <textarea class="form-control" id="st-grund" rows="2" maxlength="1000" placeholder="z. B. Erkrankung"></textarea>`,
      knoepfe: [{ text: 'Abbrechen' }, { text: knopf, art: 'gefahr', aktion: b => { grund = b.querySelector('#st-grund').value.trim(); } }],
    });
    box.closest('.modal').addEventListener('hidden.bs.modal', () => ok(grund));
  });
}
const phaseChip = p => `<span class="nl-chip ${PHASEN[p]?.[0] ?? 'grau'}">${e(PHASEN[p]?.[1] ?? p)}</span>`;
/** Status-Pill: Verwaltung und Lehrkräfte sehen die Phase (Einladungsphase), Familien den Status (angekündigt). */
const statusChip = (t, familie = false) => phaseChip(familie ? (t.status ?? t.phase) : t.phase);
const schuelerName = s => s ? `${s.name}, ${s.vorname}` : '';
const zeitraum = t => `${t.datumText} · ${t.beginn}–${t.ende} Uhr`;
const minuten = hhmm => { const [h, m] = String(hhmm || '0:0').split(':').map(Number); return h * 60 + m; };

export async function zeigen(seite, kontext, teile = []) {
  stileLaden();
  const [a, b, c] = teile;
  if (!a) return liste(seite, kontext);
  if (a === 'neu') return formular(seite, kontext, null);
  const id = Number(a);
  if (!id) return liste(seite, kontext);
  if (b === 'verwalten') return formular(seite, kontext, id);
  if (kontext.ich.rolle === 'eltern' || kontext.ich.rolle === 'schueler') return eltern(seite, kontext, id);
  if (b === 'lehrer' && c) return lehrkraft(seite, kontext, id, c);
  const info = await holen(`${BASIS}/${id}`);
  if (info.beteiligt && !info.admin) return lehrkraft(seite, kontext, id, null);
  if (info.admin) return uebersicht(seite, kontext, info);
  seite.innerHTML = `${kopfInfo(info.sprechtag, '', true)}${alarm('info', 'Sie sind an diesem Sprechtag nicht beteiligt.')}`;
  kontext.titel(info.sprechtag.name);
}

// ------------------------------------------------------------------ Startseite

/** Hinweiskarte(n) „Sprechtag“ auf der Startseite (nach „Letzte Anmeldung“). */
export async function startkarte(wurzel) {
  if (!wurzel) return;
  let karten = [];
  try { karten = await holen(`${BASIS}/start`); } catch { return; }
  if (!karten.length || !wurzel.isConnected) return;
  stileLaden();
  const html = karten.map(k => `<section class="heute-karte st-start">
      <span class="st-start-sym">${icon('aktive')}</span>
      <div><a href="#/sprechtag/${k.id}" class="st-start-titel">${e(k.name)}</a> <span class="st-start-datum">${e(k.datumText)}${k.beginn ? ` · ${e(k.beginn)}–${e(k.ende)} Uhr` : ''}${k.ort ? ` · ${e(k.ort)}` : ''}</span>${k.status === 'angekuendigt' ? '<span class="nl-chip lila st-start-chip">angekündigt</span>' : ''}
        <div class="st-start-text">${e(k.text)}</div></div>
      <a class="ms-auto fw-bold st-start-link" href="#/sprechtag/${k.id}">Zum Sprechtag</a></section>`).join('');
  const anmeldung = wurzel.querySelector('.heute-anmeldung');
  if (anmeldung) anmeldung.insertAdjacentHTML('afterend', html);
  else wurzel.insertAdjacentHTML('afterbegin', html);
}

// ------------------------------------------------------------------ Liste

async function liste(seite, kontext) {
  const d = await holen(BASIS);
  const rechts = d.admin ? `<a class="nl-hinzu" href="#/sprechtag/neu">${icon('plus')}Sprechtag anlegen</a>` : '';
  kontext.titel('Sprechtag', rechts);
  const relevant = d.sprechtage.filter(x => d.admin || d.eltern || d.schueler || x.beteiligt);
  // genau ein laufender Sprechtag: direkt öffnen
  if (!d.admin && relevant.length === 1 && !['vorbei'].includes(relevant[0].sprechtag.phase)) {
    kontext.navigieren(`#/sprechtag/${relevant[0].sprechtag.id}`, true);
    return;
  }
  if (!d.sprechtage.length) {
    seite.innerHTML = `<div class="nl-tabelle p-4">${leer(d.admin ? 'Noch kein Sprechtag angelegt.' : 'Zurzeit ist kein Sprechtag freigegeben.', 'calendar2-heart')}</div>`;
    return;
  }
  seite.innerHTML = `<div class="nl-tabelle st-liste"><table><thead><tr><th>Sprechtag</th><th>Datum</th><th>Status</th><th>Anmeldung</th><th class="text-end">Termine</th><th></th></tr></thead><tbody>
    ${d.sprechtage.map(({ sprechtag: t, beteiligt, termine }) => `<tr data-oeffnen="${t.id}" tabindex="0">
      <td><div class="nl-name">${e(t.name)}</div><div class="nl-unter">${t.lehrkraefte.length} Lehrkräfte · ${t.zeitfenster} Zeitfenster à ${t.dauer} Min.${t.ort ? ` · ${e(t.ort)}` : ''}${beteiligt ? ' · Sie sind beteiligt' : ''}</div></td>
      <td class="text-nowrap">${e(zeitraum(t))}</td><td>${statusChip(t, d.eltern || d.schueler)}</td>
      <td class="nl-unter text-nowrap">${e(zeitDe(t.anmeldungVon))} – ${e(zeitDe(t.anmeldungBis))}</td>
      <td class="text-end">${termine}</td>
      <td class="text-end text-nowrap">${d.admin ? `<a class="nl-icon sichtbar" href="#/sprechtag/${t.id}/verwalten" title="Bearbeiten" aria-label="Bearbeiten"><i class="bi bi-pencil"></i></a>
        <button type="button" class="nl-icon sichtbar rot" data-loeschen="${t.id}" title="Löschen" aria-label="Löschen"><i class="bi bi-trash"></i></button>` : ''}
        <i class="bi bi-chevron-right text-body-tertiary"></i></td></tr>`).join('')}</tbody></table></div>`;
  seite.querySelectorAll('[data-oeffnen]').forEach(tr => {
    const auf = ev => { if (ev.target.closest('a, button')) return; kontext.navigieren(`#/sprechtag/${tr.dataset.oeffnen}`); };
    tr.onclick = auf;
    tr.onkeydown = ev => { if (ev.key === 'Enter') auf(ev); };
  });
  seite.querySelectorAll('[data-loeschen]').forEach(btn => btn.onclick = async () => {
    const t = d.sprechtage.find(x => String(x.sprechtag.id) === btn.dataset.loeschen).sprechtag;
    if (!await bestaetigen('Sprechtag löschen', `„${t.name}“ mit allen Einladungen und Terminen löschen?`)) return;
    try { await api(`${BASIS}/${t.id}`, { methode: 'DELETE' }); toast('Sprechtag gelöscht.'); kontext.navigieren('#/sprechtag', true); } catch (err) { kontext.fehler(err); }
  });
}

// ------------------------------------------------------------------ Administration: anlegen / bearbeiten

const DAUERN = [5, 8, 10, 12, 15, 20, 25, 30, 45, 60];
const zeitText = m => `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`;
const datumLang = iso => {
  const m = String(iso || '').match(/^(\d{4})-(\d{2})-(\d{2})/);
  if (!m) return '';
  const d = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
  return `${['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'][d.getDay()]}, ${m[3]}.${m[2]}.${m[1]}`;
};

/** Zeitfenster wie auf dem Server: Blöcke (Lücken = Pausen), Takt Termindauer + Puffer; ein Fenster, das in eine Pause fiele, beginnt danach neu. */
function zeitfenster(bloecke, dauer, puffer) {
  const b = bloecke.filter(x => x.von && x.bis && minuten(x.bis) > minuten(x.von)).map(x => [minuten(x.von), minuten(x.bis)]).sort((x, y) => x[0] - y[0]);
  if (!b.length || !(dauer > 0)) return [];
  const pausen = b.slice(1).map((x, i) => [b[i][1], x[0]]).filter(([v, bis]) => bis > v);
  const ende = b[b.length - 1][1];
  const res = [];
  let z = b[0][0];
  while (z + dauer <= ende && res.length < 500) {
    const p = pausen.find(([v, bis]) => z < bis && z + dauer > v);
    if (p) { z = Math.max(z + 1, p[1]); continue; }
    res.push([z, z + dauer]);
    z += dauer + Math.max(0, puffer || 0);
  }
  return res;
}

async function formular(seite, kontext, id) {
  if (!kontext.ich.admin && !kontext.ich.rechte?.['sprechtag.verwalten']) { seite.innerHTML = alarm('warning', 'Nur für die Administration.'); return; }
  const [opt, vorhanden] = await Promise.all([holen(`${BASIS}/optionen`), id ? holen(`${BASIS}/${id}`) : null]);
  const t = vorhanden?.sprechtag ?? {
    name: 'Elternsprechtag', datum: '', beginn: '15:00', ende: '19:00', dauer: 10, pausen: [], zeitbloecke: [{ von: '15:00', bis: '17:00' }, { von: '17:15', bis: '19:00' }],
    einladungBis: '', anmeldungVon: '', anmeldungBis: '', sichtbar: false, raumwahl: false, hinweis: '', ort: '', lehrkraefte: [], reihe: [],
    optionen: { puffer: 0, maxTermineFamilie: 0, warteliste: false, erinnerungStunden: 24, sichtbarEltern: true, sichtbarSchueler: true, ankuendigen: true },
  };
  const o = t.optionen ?? {};
  const gewaehlt = new Map(t.lehrkraefte.map(l => [l.lehrer, l]));
  let bloecke = (t.zeitbloecke?.length ? t.zeitbloecke : [{ von: t.beginn, bis: t.ende }]).map(b => ({ ...b }));
  let tage = [];
  kontext.titel(id ? 'Sprechtag bearbeiten' : 'Sprechtag anlegen', `<a class="nl-knopf" href="#/sprechtag${id ? '/' + id : ''}">Abbrechen</a>
    <button type="button" class="nl-hinzu" data-speichern>${icon('haken')}Speichern</button>`);
  const raumOptionen = wert => `<option value="">– kein Raum –</option>${opt.raeume.map(r => `<option value="${e(r.kurz)}" ${r.kurz === wert ? 'selected' : ''} data-zweit="${e(r.name)}">${e(r.kurz)}</option>`).join('')}`;
  const dauerOptionen = wert => `<option value="0">Standard</option>${DAUERN.map(d => `<option value="${d}" ${d === wert ? 'selected' : ''}>${d} Min.</option>`).join('')}`;
  const schalter = (name, an, text) => `<label class="st-schalter"><input type="checkbox" class="form-check-input" name="${name}" ${an ? 'checked' : ''}><span>${text}</span></label>`;
  seite.innerHTML = `<form class="st-formular st-form-raster" novalidate>
   <div class="st-form-links">
    <section class="st-karte">
      <header><i class="bi bi-calendar-event"></i><h2>Allgemein</h2></header>
      <div class="row g-3">
        <div class="col-md-7"><label class="form-label" for="st-name">Name</label><input class="form-control" id="st-name" name="name" maxlength="200" value="${e(t.name)}" required></div>
        <div class="col-md-5"><label class="form-label" for="st-datum">Datum</label><input class="form-control" type="date" id="st-datum" name="datum" value="${e(t.datum)}" required></div>
        <div class="col-md-7"><label class="form-label" for="st-ort">Ort (optional)</label><input class="form-control" id="st-ort" name="ort" maxlength="300" value="${e(t.ort ?? '')}" placeholder="z. B. Hauptgebäude, 1. OG"></div>
        <div class="col-md-5">${id ? (t.reihe?.length > 1 ? `<label class="form-label">Reihe</label><div class="nl-unter st-reihe">${t.reihe.map(r => r.id === t.id ? `<b>${e(r.datumText)}</b>` : `<a href="#/sprechtag/${r.id}/verwalten">${e(r.datumText)}</a>`).join(' · ')}</div>` : '')
          : `<label class="form-label" for="st-weiterer-tag">Weitere Tage (optional)</label><div class="st-weitere"><input class="form-control" type="date" id="st-weiterer-tag" data-tag-neu aria-label="Weiteren Tag hinzufügen"><button type="button" class="nl-knopf" data-tag-plus>${icon('plus')}Tag</button></div><div class="st-weiterliste" data-tage></div>`}</div>
        <div class="col-12"><label class="form-label" for="st-hinweis">Hinweis für Lehrkräfte und Eltern (optional)</label><textarea class="form-control" id="st-hinweis" name="hinweis" rows="2" maxlength="2000">${e(t.hinweis)}</textarea></div>
      </div>
    </section>
    <section class="st-karte">
      <header><i class="bi bi-clock"></i><h2>Zeitfenster und Termine</h2><button type="button" class="nl-knopf ms-auto" data-block-neu>${icon('plus')}Zeitblock</button></header>
      <p class="nl-unter">Mehrere Zeitblöcke sind möglich (z. B. 15–17 Uhr und 17:15–19 Uhr); die Lücken dazwischen sind Pausen.</p>
      <div class="st-pausen" data-bloecke></div>
      <div class="row g-3 mt-1">
        <div class="col-6 col-md-4"><label class="form-label" for="st-dauer">Termindauer (Minuten)</label><input class="form-control" type="number" id="st-dauer" name="dauer" min="3" max="120" value="${t.dauer}"></div>
        <div class="col-6 col-md-4"><label class="form-label" for="st-puffer">Puffer danach (Minuten)</label><input class="form-control" type="number" id="st-puffer" name="puffer" min="0" max="60" value="${o.puffer ?? 0}"></div>
        <div class="col-12 col-md-4"><label class="form-label" for="st-max">Höchstens Termine je Familie</label><input class="form-control" type="number" id="st-max" name="maxTermineFamilie" min="0" max="50" value="${o.maxTermineFamilie ?? 0}"><div class="form-text">0 = unbegrenzt</div></div>
      </div>
    </section>
    <section class="st-karte">
      <header><i class="bi bi-hourglass-split"></i><h2>Ankündigung, Einladungen und Anmeldung</h2></header>
      <div class="row g-3">
        <div class="col-md-4"><label class="form-label" for="st-einladung">Einladungen der Lehrkräfte bis</label><input class="form-control" type="datetime-local" id="st-einladung" name="einladungBis" value="${e(t.einladungBis)}">
          <div class="form-text">Leer = bis zum Ende der Anmeldung.</div></div>
        <div class="col-md-4"><label class="form-label" for="st-av">Anmeldung für Eltern ab</label><input class="form-control" type="datetime-local" id="st-av" name="anmeldungVon" value="${e(t.anmeldungVon)}" required></div>
        <div class="col-md-4"><label class="form-label" for="st-ab">Anmeldung für Eltern bis</label><input class="form-control" type="datetime-local" id="st-ab" name="anmeldungBis" value="${e(t.anmeldungBis)}" required></div>
        <div class="col-md-6">${schalter('sichtbar', t.sichtbar, '<b>Freigegeben</b> – für Lehrkräfte und Familien sichtbar (Startseite und Menü)')}</div>
        <div class="col-md-6">${schalter('ankuendigen', o.ankuendigen ?? true, 'Schon vor der Anmeldung <b>ankündigen</b> (Datum, Zeit, Ort, Anmeldebeginn)')}</div>
        <div class="col-md-6">${schalter('sichtbarEltern', o.sichtbarEltern ?? true, 'Für <b>Erziehungsberechtigte</b> sichtbar (Buchung)')}</div>
        <div class="col-md-6">${schalter('sichtbarSchueler', o.sichtbarSchueler ?? true, 'Für <b>Schüler*innen</b> sichtbar (nur Information und eigene Termine)')}</div>
      </div>
    </section>
    <section class="st-karte">
      <header><i class="bi bi-sliders"></i><h2>Weitere Optionen</h2></header>
      <div class="row g-3">
        <div class="col-md-6">${schalter('raumwahl', t.raumwahl, 'Lehrkräfte dürfen ihren <b>Raum</b> bzw. ihren Online-Link <b>selbst wählen</b>')}</div>
        <div class="col-md-6">${schalter('warteliste', o.warteliste, '<b>Warteliste</b> bei ausgebuchten Lehrkräften (Mitteilung, sobald ein Termin frei wird)')}</div>
        <div class="col-md-6"><label class="form-label" for="st-erinnerung">Erinnerung an Termine</label><select class="form-select" id="st-erinnerung" name="erinnerungStunden">
          ${[[0, 'keine Erinnerung'], [2, '2 Stunden vorher'], [24, '1 Tag vorher'], [48, '2 Tage vorher'], [72, '3 Tage vorher'], [168, '1 Woche vorher']].map(([w, x]) => `<option value="${w}" ${w === (o.erinnerungStunden ?? 0) ? 'selected' : ''}>${x}</option>`).join('')}</select>
          <div class="form-text">Mitteilung an Familien und Lehrkräfte mit Termin.</div></div>
      </div>
    </section>
    <section class="st-karte">
      <header><i class="bi bi-people"></i><h2>Beteiligte Lehrkräfte</h2><span class="nl-zahl ms-2" data-anzahl></span>
        <span class="ms-auto d-flex gap-2"><button type="button" class="nl-knopf" data-alle>Alle</button><button type="button" class="nl-knopf" data-keine>Keine</button></span></header>
      <div class="nl-leiste"><label class="nl-suche"><i class="bi bi-search" aria-hidden="true"></i><input type="search" data-suche placeholder="Lehrkraft suchen" aria-label="Lehrkraft suchen"></label>
        <span class="nl-unter">Alle wählen und einzelne abwählen schließt Lehrkräfte aus.</span></div>
      <div class="nl-tabelle st-lehrerwahl"><table><thead><tr><th class="nl-wahl"><span class="visually-hidden">Beteiligt</span></th><th>Lehrkraft</th><th>Raum</th><th>Dauer</th><th>Online</th></tr></thead><tbody>
        ${opt.lehrer.map(l => { const g = gewaehlt.get(l.kurz); return `<tr data-lehrer="${e(l.kurz)}" data-text="${e(`${l.kurz} ${l.name}`.toLowerCase())}" class="${g ? 'nl-gewaehlt' : ''}">
          <td class="nl-wahl"><input type="checkbox" class="nl-rund" data-wahl ${g ? 'checked' : ''} aria-label="${e(l.name)} beteiligt"></td>
          <td><span class="nl-name">${e(l.name)}</span> <span class="nl-tag">${e(l.kurz)}</span></td>
          <td class="st-raumzelle"><select class="form-select form-select-sm" data-raum aria-label="Raum ${e(l.kurz)}">${raumOptionen(g?.raum ?? '')}</select></td>
          <td class="st-dauerzelle"><select class="form-select form-select-sm" data-dauer aria-label="Termindauer ${e(l.kurz)}">${dauerOptionen(g?.dauer ?? 0)}</select></td>
          <td class="st-onlinezelle"><span class="st-online"><input type="checkbox" class="form-check-input" data-online ${g?.online ? 'checked' : ''} aria-label="${e(l.kurz)} online">
            <input class="form-control form-control-sm" data-link maxlength="500" placeholder="https://…" value="${e(g?.link ?? '')}" ${g?.online ? '' : 'hidden'} aria-label="Link ${e(l.kurz)}"></span></td></tr>`; }).join('')}
      </tbody></table></div>
    </section>
   </div>
   <aside class="st-form-rechts" aria-label="Vorschau">
    <section class="st-karte st-vorschau-karte">
      <header><i class="bi bi-eye"></i><h2>Vorschau</h2></header>
      <div data-vorschau></div>
    </section>
   </aside>
  </form>`;
  const form = seite.querySelector('form');
  const bloeckeZeichnen = () => {
    const box = form.querySelector('[data-bloecke]');
    box.innerHTML = bloecke.map((p, i) => `<div class="st-pause" data-i="${i}">
        <label class="u-feld"><span>Von</span><input class="form-control" type="time" data-p="von" value="${e(p.von)}"></label>
        <label class="u-feld"><span>Bis</span><input class="form-control" type="time" data-p="bis" value="${e(p.bis)}"></label>
        ${bloecke.length > 1 ? `<button type="button" class="nl-icon sichtbar rot" data-block-weg="${i}" title="Zeitblock entfernen" aria-label="Zeitblock entfernen"><i class="bi bi-trash"></i></button>` : ''}</div>`).join('');
    box.querySelectorAll('[data-p]').forEach(inp => inp.oninput = () => { bloecke[Number(inp.closest('[data-i]').dataset.i)][inp.dataset.p] = inp.value; vorschau(); });
    box.querySelectorAll('[data-block-weg]').forEach(b => b.onclick = () => { bloecke.splice(Number(b.dataset.blockWeg), 1); bloeckeZeichnen(); vorschau(); });
  };
  const tageZeichnen = () => {
    const box = form.querySelector('[data-tage]');
    if (!box) return;
    box.innerHTML = tage.map((d, i) => `<span class="nl-chip grau">${e(datumLang(d))}<button type="button" class="st-chip-x" data-tag-weg="${i}" aria-label="Tag entfernen"><i class="bi bi-x"></i></button></span>`).join('');
    box.querySelectorAll('[data-tag-weg]').forEach(b => b.onclick = () => { tage.splice(Number(b.dataset.tagWeg), 1); tageZeichnen(); vorschau(); });
  };
  const gewaehlteZeilen = () => [...form.querySelectorAll('[data-lehrer]')].filter(tr => tr.querySelector('[data-wahl]').checked);
  const vorschau = () => {
    const dauer = Number(form.dauer.value) || 0, puffer = Number(form.puffer.value) || 0;
    const fenster = zeitfenster(bloecke, dauer, puffer);
    const zeilen = gewaehlteZeilen();
    const kapazitaet = zeilen.reduce((n, tr) => n + zeitfenster(bloecke, Number(tr.querySelector('[data-dauer]').value) || dauer, puffer).length, 0);
    const abweichend = zeilen.filter(tr => Number(tr.querySelector('[data-dauer]').value) > 0).length;
    const online = zeilen.filter(tr => tr.querySelector('[data-online]').checked).length;
    form.querySelector('[data-anzahl]').innerHTML = `<b>${zeilen.length}</b> gewählt`;
    const gueltig = bloecke.filter(x => x.von && x.bis && minuten(x.bis) > minuten(x.von)).sort((a, b) => minuten(a.von) - minuten(b.von));
    const pausen = gueltig.slice(1).map((x, i) => `${gueltig[i].bis}–${x.von}`).filter(p => p.split('–')[0] < p.split('–')[1]);
    // Zeitleiste der Phasen: freigegeben/angekündigt → Anmeldung → Sprechtag
    const phasen = [
      form.sichtbar.checked && form.ankuendigen.checked ? ['lila', 'Angekündigt', 'sobald freigegeben'] : null,
      form.anmeldungVon.value ? ['gruen', 'Anmeldung', `${zeitDe(form.anmeldungVon.value)} – ${zeitDe(form.anmeldungBis.value) || '…'}`] : null,
      form.datum.value ? ['gelb', 'Sprechtag', [datumLang(form.datum.value), ...tage.map(datumLang), ...(t.reihe ?? []).filter(r => r.id !== t.id).map(r => r.datumText)].join(' · ')] : null,
    ].filter(Boolean);
    form.querySelector('[data-vorschau]').innerHTML = `
      <div class="nl-zahlen st-vzahlen"><span class="nl-zahl"><b>${fenster.length}</b> Termine je Lehrkraft</span><span class="nl-zahl gelb"><b>${kapazitaet}</b> Termine gesamt</span></div>
      <div class="st-vzeile">${form.datum.value ? `<b>${e(datumLang(form.datum.value))}</b>${tage.length ? ` <span class="nl-unter">+ ${tage.length} weitere${tage.length === 1 ? 'r Tag' : ' Tage'}</span>` : ''}` : '<span class="nl-unter">Datum fehlt</span>'}</div>
      <div class="st-vzeile nl-unter">${gueltig.length ? `${e(gueltig[0].von)}–${e(gueltig[gueltig.length - 1].bis)} Uhr` : 'Zeitfenster fehlt'}${pausen.length ? ` · Pause ${e(pausen.join(', '))}` : ''}${form.ort.value ? ` · ${e(form.ort.value)}` : ''}</div>
      <div class="st-vzeile nl-unter">Termine à ${dauer} Min.${puffer ? ` + ${puffer} Min. Puffer` : ''}${abweichend ? ` · ${abweichend} mit eigener Dauer` : ''}${online ? ` · ${online} online` : ''}${Number(form.maxTermineFamilie.value) > 0 ? ` · max. ${Number(form.maxTermineFamilie.value)} je Familie` : ''}</div>
      ${phasen.length ? `<ol class="st-phasen">${phasen.map(([f, n, x]) => `<li><span class="nl-chip ${f}">${e(n)}</span><span class="nl-unter">${e(x)}</span></li>`).join('')}</ol>` : ''}
      <div class="st-slots" aria-label="Zeitslots">${fenster.length ? fenster.map(([v, b], i) => {
        // Pause aus den Zeitblöcken (Lücke zwischen zwei Blöcken vor diesem Zeitfenster)
        const luecke = i > 0 ? gueltig.slice(1).map((x, j) => [minuten(gueltig[j].bis), minuten(x.von)]).find(([pv, pb]) => pb > pv && pv >= fenster[i - 1][1] - puffer && pb <= v) : null;
        const pause = luecke ? `<div class="st-slot-pause"><i class="bi bi-cup-hot"></i>Pause ${zeitText(luecke[0])}–${zeitText(luecke[1])}</div>` : '';
        return `${pause}<div class="st-slot"><span class="st-zeit">${zeitText(v)}</span><span class="nl-unter">bis ${zeitText(b)}</span></div>`;
      }).join('') : '<div class="nl-unter p-2">Keine Zeitfenster – bitte Zeitblock und Termindauer prüfen.</div>'}</div>`;
  };
  form.querySelector('[data-block-neu]').onclick = () => {
    const letzt = bloecke[bloecke.length - 1];
    const start = letzt?.bis ? minuten(letzt.bis) + 15 : 15 * 60;
    bloecke.push({ von: zeitText(Math.min(start, 23 * 60)), bis: zeitText(Math.min(start + 60, 23 * 60 + 59)) });
    bloeckeZeichnen(); vorschau();
  };
  const tagPlus = form.querySelector('[data-tag-plus]');
  if (tagPlus) tagPlus.onclick = () => {
    const d = form.querySelector('[data-tag-neu]').value;
    if (!d || tage.includes(d) || d === form.datum.value) return;
    tage.push(d); tage.sort();
    form.querySelector('[data-tag-neu]').value = '';
    tageZeichnen(); vorschau();
  };
  form.addEventListener('input', ev => { if (!ev.target.closest('[data-bloecke]') && !ev.target.matches('[data-suche]')) vorschau(); });
  form.addEventListener('change', () => vorschau());
  form.querySelectorAll('[data-wahl]').forEach(c => c.onchange = () => { c.closest('tr').classList.toggle('nl-gewaehlt', c.checked); vorschau(); });
  form.querySelectorAll('[data-online]').forEach(c => c.addEventListener('change', () => {
    const tr = c.closest('tr');
    tr.querySelector('[data-link]').hidden = !c.checked;
    if (c.checked && !tr.querySelector('[data-wahl]').checked) { tr.querySelector('[data-wahl]').checked = true; tr.classList.add('nl-gewaehlt'); }
  }));
  const sichtbare = () => [...form.querySelectorAll('[data-lehrer]')].filter(tr => !tr.hidden);
  form.querySelector('[data-alle]').onclick = () => { sichtbare().forEach(tr => { tr.querySelector('[data-wahl]').checked = true; tr.classList.add('nl-gewaehlt'); }); vorschau(); };
  form.querySelector('[data-keine]').onclick = () => { sichtbare().forEach(tr => { tr.querySelector('[data-wahl]').checked = false; tr.classList.remove('nl-gewaehlt'); }); vorschau(); };
  form.querySelector('[data-suche]').oninput = ev => {
    const q = ev.target.value.trim().toLowerCase();
    form.querySelectorAll('[data-lehrer]').forEach(tr => { tr.hidden = !!q && !tr.dataset.text.includes(q); });
  };
  // Raum bzw. eigene Dauer gewählt → Lehrkraft automatisch beteiligt
  form.querySelectorAll('[data-raum], [data-dauer]').forEach(s => s.addEventListener('change', () => {
    if (!s.value || s.value === '0') return;
    const c = s.closest('tr').querySelector('[data-wahl]');
    if (!c.checked) { c.checked = true; s.closest('tr').classList.add('nl-gewaehlt'); vorschau(); }
  }));
  bloeckeZeichnen();
  tageZeichnen();
  vorschau();
  form.onsubmit = ev => ev.preventDefault();
  document.querySelector('[data-speichern]').onclick = async ev => {
    const btn = ev.currentTarget;
    const zb = bloecke.filter(p => p.von || p.bis);
    const daten = {
      name: form.name.value, datum: form.datum.value, ort: form.ort.value, dauer: Number(form.dauer.value) || 10,
      beginn: zb[0]?.von ?? '', ende: zb[zb.length - 1]?.bis ?? '', pausen: [], zeitbloecke: zb,
      einladungBis: form.einladungBis.value, anmeldungVon: form.anmeldungVon.value, anmeldungBis: form.anmeldungBis.value,
      sichtbar: form.sichtbar.checked, raumwahl: form.raumwahl.checked, hinweis: form.hinweis.value,
      optionen: {
        puffer: Number(form.puffer.value) || 0, maxTermineFamilie: Number(form.maxTermineFamilie.value) || 0, warteliste: form.warteliste.checked,
        erinnerungStunden: Number(form.erinnerungStunden.value) || 0, sichtbarEltern: form.sichtbarEltern.checked, sichtbarSchueler: form.sichtbarSchueler.checked,
        ankuendigen: form.ankuendigen.checked,
      },
      weitereTage: id ? null : tage,
      lehrkraefte: gewaehlteZeilen().map(tr => ({
        lehrer: tr.dataset.lehrer, raum: tr.querySelector('[data-raum]').value, dauer: Number(tr.querySelector('[data-dauer]').value) || 0,
        online: tr.querySelector('[data-online]').checked, link: tr.querySelector('[data-link]').value,
      })),
    };
    btn.disabled = true;
    try {
      const r = await senden(id ? `${BASIS}/${id}` : BASIS, daten, id ? 'PUT' : 'POST');
      toast(id ? 'Sprechtag gespeichert.' : tage.length ? `Sprechtag mit ${tage.length + 1} Tagen angelegt.` : 'Sprechtag angelegt.');
      kontext.navigieren(`#/sprechtag/${r.id}`);
    } catch (err) { kontext.fehler(err); }
    finally { btn.disabled = false; }
  };
}

// ------------------------------------------------------------------ Administration: Übersicht je Lehrkraft

async function uebersicht(seite, kontext, info) {
  const t = info.sprechtag;
  kontext.titel(t.name, `<a class="nl-knopf" href="${BASIS}/${t.id}/plan.pdf" target="_blank" rel="noopener">${icon('file-earmark-pdf')}Gesamtplan (PDF)</a>
    <a class="nl-knopf" href="${BASIS}/${t.id}/plan.pdf?leer=true" target="_blank" rel="noopener">${icon('printer')}Aushang</a>
    ${absagbar(t) ? `<button type="button" class="nl-knopf rot" data-absagen>${icon('x')}Sprechtag absagen</button>
    <a class="nl-hinzu" href="#/sprechtag/${t.id}/verwalten">${icon('stift')}Bearbeiten</a>` : ''}`);
  document.querySelector('[data-absagen]')?.addEventListener('click', async () => {
    const grund = await absageDialog('Sprechtag absagen', `„${t.name}“ am ${t.datumText} absagen? Alle gebuchten Termine entfallen; alle beteiligten Lehrkräfte und Erziehungsberechtigten werden per Mitteilung benachrichtigt.`, 'Sprechtag absagen');
    if (grund === null) return;
    try {
      const r = await senden(`${BASIS}/${t.id}/absagen`, { grund });
      toast(`Sprechtag abgesagt · ${r.termine} Termine entfallen · ${r.benachrichtigt} Konten benachrichtigt.`);
      kontext.navigieren(location.hash);
    } catch (err) { kontext.fehler(err); }
  });
  const abgesagt = new Set(t.lehrkraefte.filter(l => l.abgesagt).map(l => l.lehrer));
  const summe = k => info.uebersicht.reduce((n, l) => n + l[k], 0);
  seite.innerHTML = `${kopfInfo(t)}
    <div class="nl-zahlen"><span class="nl-zahl"><b>${info.uebersicht.length}</b> Lehrkräfte</span><span class="nl-zahl gelb"><b>${summe('gebucht')}</b> Termine</span>
      <span class="nl-zahl"><b>${summe('frei')}</b> frei</span><span class="nl-zahl rot"><b>${summe('gesperrt')}</b> gesperrt</span><span class="nl-zahl lila"><b>${summe('einladungen')}</b> Einladungen</span></div>
    <div class="nl-tabelle"><table><thead><tr><th>Lehrkraft</th><th>Raum</th><th class="text-end">Termine</th><th class="text-end">Frei</th><th class="text-end">Gesperrt</th><th class="text-end">Einladungen</th><th></th></tr></thead><tbody>
      ${info.uebersicht.map(l => `<tr data-lehrer="${e(l.lehrer)}" tabindex="0"><td><span class="nl-name">${e(l.name)}</span> <span class="nl-tag">${e(l.lehrer)}</span>${abgesagt.has(l.lehrer) ? ' <span class="nl-chip rot">abgesagt</span>' : ''}</td>
        <td>${l.raum ? e(l.raum) : '<span class="nl-unter">–</span>'}</td><td class="text-end">${l.gebucht}</td><td class="text-end">${l.frei}</td><td class="text-end">${l.gesperrt}</td><td class="text-end">${l.einladungen}</td>
        <td class="text-end text-nowrap"><a class="nl-icon sichtbar" href="${BASIS}/${t.id}/plan.pdf?lehrer=${encodeURIComponent(l.lehrer)}" data-pdf="hoch" target="_blank" rel="noopener" title="Terminplan (PDF)" aria-label="Terminplan als PDF"><i class="bi bi-file-earmark-pdf"></i></a>
          <i class="bi bi-chevron-right text-body-tertiary"></i></td></tr>`).join('')}</tbody></table></div>`;
  seite.querySelectorAll('tr[data-lehrer]').forEach(tr => {
    const auf = ev => { if (ev.target.closest('a')) return; kontext.navigieren(`#/sprechtag/${t.id}/lehrer/${encodeURIComponent(tr.dataset.lehrer)}`); };
    tr.onclick = auf;
    tr.onkeydown = ev => { if (ev.key === 'Enter') auf(ev); };
  });
}

/**
 * Kopfzeile mit Datum, Zeit, Phase und Hinweis – zwei gleich hohe Zeilen: oben Datum, Status-Pill und Phasentext (auf einer Linie),
 * darunter Uhrzeit/Termindauer/Pause und der Zusatz (z. B. Lehrkraft und Raum).
 */
function kopfInfo(t, zusatz = '', familie = false) {
  const phaseText = familie && t.status === 'angekuendigt' ? `Anmeldung ab ${zeitDe(t.anmeldungVon)}` : {
    einladung: `Einladungen bis ${zeitDe(t.einladungEnde)} · Anmeldung für Eltern ab ${zeitDe(t.anmeldungVon)}`,
    anmeldung: `Anmeldung für Eltern bis ${zeitDe(t.anmeldungBis)}`,
    geschlossen: 'Die Anmeldung ist geschlossen.', vorbei: 'Der Sprechtag ist vorbei.', vorbereitung: 'Noch nicht für Lehrkräfte und Eltern sichtbar.',
    abgesagt: `Der Sprechtag wurde abgesagt${t.absageGrund ? ` (${t.absageGrund})` : ''}. Alle Termine entfallen.`,
  }[t.phase] ?? '';
  return `<div class="st-kopf">
      <div class="st-kopf-zeile"><b class="st-kopf-datum">${e(t.datumText)}</b>${statusChip(t, familie)}${phaseText ? `<span class="st-kopf-phase">${e(phaseText)}</span>` : ''}</div>
      <div class="st-kopf-zeile st-kopf-unter"><span>${e(t.beginn)}–${e(t.ende)} Uhr${t.ort ? ` · ${e(t.ort)}` : ''} · Termine à ${t.dauer} Min.${t.optionen?.puffer ? ` (+${t.optionen.puffer} Min. Puffer)` : ''}${t.pausen.length ? ` · Pause ${t.pausen.map(p => `${p.von}–${p.bis}`).join(', ')}` : ''}${t.reihe?.length > 1 ? ` · Tage: ${t.reihe.map(r => r.id === t.id ? e(r.datumText) : `<a href="#/sprechtag/${r.id}">${e(r.datumText)}</a>`).join(', ')}` : ''}</span>${zusatz}</div></div>
    ${t.hinweis ? `<div class="nl-hinweis"><i class="bi bi-info-circle"></i>${e(t.hinweis)}</div>` : ''}`;
}

// ------------------------------------------------------------------ Lehrkraft

async function lehrkraft(seite, kontext, id, lehrer) {
  const q = lehrer ? `?lehrer=${encodeURIComponent(lehrer)}` : '';
  const qa = lehrer ? `&lehrer=${encodeURIComponent(lehrer)}` : '';
  const [d, sch] = await Promise.all([holen(`${BASIS}/${id}/lehrkraft${q}`), holen(`${BASIS}/${id}/schueler${q}`)]);
  const t = d.sprechtag;
  const neu = () => lehrkraft(seite, kontext, id, lehrer);
  const mitLehrer = obj => (lehrer ? { ...obj, lehrer } : obj);
  const teilnahme = !absagbar(t) ? '' : d.lehrer.abgesagt
    ? `<button type="button" class="nl-knopf gruen" data-teilnahme="wiederaufnehmen"><i class="bi bi-arrow-counterclockwise"></i>Teilnahme wieder aufnehmen</button>`
    : `<button type="button" class="nl-knopf rot" data-teilnahme="absagen">${icon('x')}Teilnahme absagen</button>`;
  kontext.titel(lehrer ? `${t.name} · ${d.lehrer.name}` : t.name, `${teilnahme}${d.admin ? `<a class="nl-knopf" href="#/sprechtag/${id}">${icon('zurueck')}Übersicht</a>` : ''}`);
  document.querySelector('[data-teilnahme]')?.addEventListener('click', async ev => {
    if (ev.currentTarget.dataset.teilnahme === 'wiederaufnehmen') {
      try { await senden(`${BASIS}/${id}/teilnahme/wiederaufnehmen`, mitLehrer({})); toast('Teilnahme wieder aufgenommen – die Zeitfenster sind wieder buchbar.'); neu(); } catch (err) { kontext.fehler(err); }
      return;
    }
    const gebuchte = d.zeitfenster.filter(z => z.status === 'gebucht').length;
    const grund = await absageDialog('Teilnahme absagen', `${lehrer ? d.lehrer.name : 'Ihre'} Teilnahme am Sprechtag „${t.name}“ absagen? ${gebuchte ? `${gebuchte} gebuchte(r) Termin(e) entfallen; die Erziehungsberechtigten werden per Mitteilung benachrichtigt.` : 'Es sind noch keine Termine gebucht.'}`, 'Teilnahme absagen');
    if (grund === null) return;
    try {
      const r = await senden(`${BASIS}/${id}/teilnahme/absagen`, mitLehrer({ grund }));
      toast(`Teilnahme abgesagt · ${r.termine} Termine entfallen · ${r.benachrichtigt} Konten benachrichtigt.`);
      neu();
    } catch (err) { kontext.fehler(err); }
  });
  const gebucht = d.zeitfenster.filter(z => z.status === 'gebucht').length;
  const gesperrt = d.zeitfenster.filter(z => z.status === 'gesperrt').length;
  const klassen = sch.klassen;
  let klasse = sch.eigeneKlassen[0] ?? '';

  const raumPanel = d.darfRaum ? `<section class="st-karte st-raum"><header><i class="bi bi-door-open"></i><h2>Raum und Online-Gespräche</h2></header>
      <label class="u-feld"><span>Raum</span><select class="form-select" data-raumwahl data-platzhalter="Raum wählen"><option value="">– kein Raum –</option>
        ${d.raeume.map(r => `<option value="${e(r.kurz)}" ${r.kurz === d.lehrer.raum ? 'selected' : ''} data-zweit="${e(r.name)}">${e(r.kurz)}</option>`).join('')}</select></label>
      <label class="st-schalter mt-3"><input type="checkbox" class="form-check-input" data-online ${d.lehrer.online ? 'checked' : ''}><span>Gespräche finden <b>online</b> statt</span></label>
      <div class="st-online mt-2" data-linkbox ${d.lehrer.online ? '' : 'hidden'}><input class="form-control" data-link maxlength="500" placeholder="https://… (Link zur Videokonferenz)" value="${e(d.lehrer.link ?? '')}" aria-label="Link zur Videokonferenz">
        <button type="button" class="nl-knopf" data-link-speichern>Speichern</button></div>
      <div class="nl-unter mt-1" data-linkbox ${d.lehrer.online ? '' : 'hidden'}>Den Link sehen nur Familien mit Termin bei Ihnen.</div></section>`
    : d.lehrer.online ? `<div class="nl-hinweis"><i class="bi bi-camera-video"></i>Ihre Gespräche finden online statt${d.lehrer.link ? `: <span class="st-link">${e(d.lehrer.link)}</span>` : ''}.</div>` : '';
  const wartePanel = d.warteliste?.length ? `<section class="st-karte"><header><i class="bi bi-hourglass"></i><h2>Warteliste</h2><span class="nl-zahl"><b>${d.warteliste.length}</b></span></header>
      <div class="nl-tabelle"><table><thead><tr><th>Klasse</th><th>Schüler*in</th><th>Status</th><th></th></tr></thead><tbody>
      ${d.warteliste.map(w => `<tr><td>${e(w.schueler.klasse)}</td><td class="nl-name st-n">${e(schuelerName(w.schueler))}</td>
        <td>${w.benachrichtigt ? '<span class="nl-chip gruen">benachrichtigt</span>' : '<span class="nl-chip lila">wartet</span>'}</td>
        <td class="text-end"><button type="button" class="nl-icon sichtbar rot" data-warte-weg="${w.id}" title="Von der Warteliste nehmen" aria-label="Von der Warteliste nehmen"><i class="bi bi-x-lg"></i></button></td></tr>`).join('')}
      </tbody></table></div></section>` : '';

  const einladungZeilen = d.einladungen.length ? `<table><thead><tr><th>Klasse</th><th>Schüler*in</th><th>Status</th><th><span class="visually-hidden">Löschen</span></th></tr></thead><tbody>
      ${d.einladungen.slice().sort((x, y) => (x.schueler.klasse || '').localeCompare(y.schueler.klasse || '', 'de') || schuelerName(x.schueler).localeCompare(schuelerName(y.schueler), 'de')).map(x => `<tr>
        <td>${e(x.schueler.klasse)}</td><td class="nl-name st-n">${e(schuelerName(x.schueler))}</td>
        <td>${x.termin ? `<span class="nl-chip gruen"><i class="bi bi-check2"></i>Termin ${e(x.termin)}</span>` : x.benachrichtigt ? '<span class="nl-chip lila">eingeladen</span>' : '<span class="nl-chip gelb" data-hover="Kein Elternkonto – bitte anders benachrichtigen" data-hover-art="dunkel">ohne Elternkonto</span>'}</td>
        <td class="text-end"><button type="button" class="nl-icon sichtbar rot" data-einladung-weg="${x.id}" title="Einladung löschen" aria-label="Einladung löschen"><i class="bi bi-x-lg"></i></button></td></tr>`).join('')}</tbody></table>`
    : `<div class="p-3 nl-unter">Nichts anzuzeigen – noch keine Einladungen.</div>`;

  const schuelerAuswahl = () => {
    const liste = sch.schueler.filter(s => !klasse || s.klasse === klasse);
    const eingeladen = new Set(d.einladungen.map(x => x.schueler.id));
    return `<select class="form-select" multiple data-schueler data-platzhalter="Bitte wählen Sie" aria-label="Schüler*innen">
      ${liste.map(s => `<option value="${s.id}" ${eingeladen.has(s.id) ? 'disabled' : ''} data-zweit="${e(`${s.klasse}${s.elternkonto ? '' : ' · ohne Elternkonto'}${eingeladen.has(s.id) ? ' · bereits eingeladen' : ''}`)}">${e(schuelerName(s))}</option>`).join('')}</select>`;
  };

  const zeile = z => {
    const x = z.termin;
    const status = z.status === 'gebucht' ? '' : z.status === 'gesperrt' ? '<span class="st-abwesend">abwesend</span>' : '<span class="nl-unter">frei</span>';
    return `<tr class="st-${z.status}" data-beginn="${z.beginn}">
      <td class="nl-wahl">${z.status === 'gebucht' ? '' : `<input type="checkbox" class="nl-rund" data-fenster="${z.beginn}" aria-label="Zeitfenster ${z.beginn} wählen">`}</td>
      <td><span class="st-zeit">${e(z.beginn)}</span></td>
      <td>${x ? `<span class="nl-name st-n">${e(schuelerName(x.schueler))}</span>` : status}</td>
      <td>${x ? e(x.schueler.klasse) : ''}</td>
      <td class="nl-unter">${x ? e([x.gebuchtVon ? `gebucht von ${x.gebuchtVon}` : (x.quelle === 'eltern' ? '' : 'eingetragen'), x.bemerkung].filter(Boolean).join(' · ')) : ''}</td>
      <td class="text-end text-nowrap">${x
        ? `<button type="button" class="nl-icon sichtbar" data-verschieben="${x.id}" title="Verschieben" aria-label="Termin verschieben"><i class="bi bi-arrow-left-right"></i></button>
           <button type="button" class="nl-icon sichtbar rot" data-storno="${x.id}" title="Absagen" aria-label="Termin absagen"><i class="bi bi-x-lg"></i></button>`
        : z.status === 'frei' ? `<button type="button" class="nl-icon sichtbar" data-eintragen="${z.beginn}" title="Termin eintragen" aria-label="Termin um ${z.beginn} eintragen"><i class="bi bi-plus-lg"></i></button>
           <button type="button" class="nl-icon sichtbar" data-sperren="${z.beginn}" title="Sperren (abwesend)" aria-label="Zeitfenster ${z.beginn} sperren"><i class="bi bi-lock"></i></button>`
          : `<button type="button" class="nl-icon sichtbar" data-freigeben="${z.beginn}" title="Freigeben" aria-label="Zeitfenster ${z.beginn} freigeben"><i class="bi bi-unlock"></i></button>`}</td></tr>`;
  };

  seite.innerHTML = `${kopfInfo(t, `<span>· ${e(d.lehrer.name)}${d.lehrer.raum ? ` · Raum ${e(d.lehrer.raum)}` : ''}</span>`)}
    ${d.lehrer.abgesagt ? `<div class="nl-hinweis rot"><i class="bi bi-x-circle"></i>Die Teilnahme wurde am ${e(zeitDe(d.lehrer.abgesagt.replace(' ', 'T')))} abgesagt${d.lehrer.absageGrund ? ` (${e(d.lehrer.absageGrund)})` : ''}. Es können keine Termine gebucht werden.</div>` : ''}
    <div class="st-raster">
      <div class="st-links">
        ${raumPanel}
        ${wartePanel}
        <section class="st-karte st-einladen"><header><i class="bi bi-envelope-paper"></i><h2>Gespräche wünschen</h2><span class="nl-zahl st-rechts"><b>${d.einladungen.length}</b> Einladungen</span></header>
          ${d.einladenMoeglich ? `<div class="st-einladen-felder">
            <label class="u-feld st-klassefeld"><span>Klasse</span><select class="form-select" data-klasse aria-label="Klasse"><option value="">alle Klassen</option>
              ${klassen.map(k => `<option value="${e(k)}" ${k === klasse ? 'selected' : ''} ${sch.eigeneKlassen.includes(k) ? 'data-zweit="eigene Klasse"' : ''}>${e(k)}</option>`).join('')}</select></label>
            <label class="u-feld st-schuelerfeld"><span>Schüler*in</span><span data-schuelerbox>${schuelerAuswahl()}</span></label>
            <label class="u-feld st-textfeld"><span>Nachricht an die Eltern (optional)</span><input class="form-control" data-text maxlength="1000" placeholder="z. B. Anlass des Gesprächs"></label>
            <button type="button" class="nl-hinzu" data-einladen>${icon('senden')}Anfordern</button></div>`
          : `<div class="nl-hinweis m-3"><i class="bi bi-info-circle"></i>${t.phase === 'vorbereitung' ? 'Der Sprechtag ist noch nicht freigegeben.' : 'Die Einladungsphase ist beendet.'}</div>`}
          <div class="nl-tabelle st-einladungen">${einladungZeilen}</div>
        </section>
      </div>
      <section class="st-karte st-termine">
        <header><i class="bi bi-person-video3"></i><h2>Meine Termine</h2><span class="nl-zahl gelb"><b>${gebucht}</b> gebucht</span>${gesperrt ? `<span class="nl-zahl rot"><b>${gesperrt}</b> gesperrt</span>` : ''}
          <span class="st-kopf-knoepfe">
            <a class="nl-knopf" href="${BASIS}/${id}/plan.pdf${q}" data-pdf="hoch" target="_blank" rel="noopener" title="Liste der gebuchten Termine als PDF">${icon('file-earmark-pdf')}Terminliste</a>
            <a class="nl-knopf" href="${BASIS}/${id}/plan.pdf?leer=true${qa}" data-pdf="hoch" target="_blank" rel="noopener" title="Alle Zeitfenster als PDF, z. B. zum Aushang an der Tür">${icon('printer')}Aushang</a></span></header>
        <div class="nl-tabelle st-zeitraster"><table><thead><tr><th class="nl-wahl"><span class="visually-hidden">Wahl</span></th><th>Termin</th><th>Schüler*in</th><th>Klasse</th><th>Hinweis</th><th></th></tr></thead>
          <tbody>${d.zeitfenster.map(zeile).join('')}</tbody></table>
          <div class="nl-fuss"><span class="nl-unter" data-gewaehlt>Zeitfenster wählen, um sie zu sperren (abwesend) oder freizugeben.</span>
            <button type="button" class="nl-knopf rot ms-auto" data-sammel="sperren" disabled>${icon('schloss')}Sperren</button>
            <button type="button" class="nl-knopf gruen" data-sammel="freigeben" disabled><i class="bi bi-unlock"></i>Freigeben</button></div></div>
      </section>
    </div>`;

  // Raum, Online-Gespräche mit Link, Warteliste
  seite.querySelector('[data-raumwahl]')?.addEventListener('change', async ev => {
    try { await senden(`${BASIS}/${id}/raum`, mitLehrer({ raum: ev.target.value }), 'PUT'); toast('Raum gespeichert.'); neu(); } catch (err) { kontext.fehler(err); }
  });
  const online = seite.querySelector('[data-online]');
  const onlineSpeichern = async () => {
    try {
      await senden(`${BASIS}/${id}/raum`, mitLehrer({ online: online.checked, link: seite.querySelector('[data-link]').value }), 'PUT');
      toast(online.checked ? 'Online-Gespräche gespeichert.' : 'Gespräche wieder vor Ort.');
      neu();
    } catch (err) { kontext.fehler(err); }
  };
  online?.addEventListener('change', () => {
    seite.querySelectorAll('[data-linkbox]').forEach(x => { x.hidden = !online.checked; });
    if (!online.checked) onlineSpeichern();
  });
  seite.querySelector('[data-link-speichern]')?.addEventListener('click', onlineSpeichern);
  seite.querySelectorAll('[data-warte-weg]').forEach(b => b.onclick = async () => {
    try { await api(`${BASIS}/${id}/warteliste/${b.dataset.warteWeg}`, { methode: 'DELETE' }); toast('Von der Warteliste genommen.'); neu(); } catch (err) { kontext.fehler(err); }
  });

  // Einladungen
  const klasseSel = seite.querySelector('[data-klasse]');
  if (klasseSel) klasseSel.addEventListener('change', () => { klasse = klasseSel.value; seite.querySelector('[data-schuelerbox]').innerHTML = schuelerAuswahl(); });
  seite.querySelector('[data-einladen]')?.addEventListener('click', async ev => {
    const ids = [...seite.querySelector('[data-schueler]').selectedOptions].map(o => Number(o.value));
    if (!ids.length) { kontext.fehler(new Error('Bitte mindestens eine Schülerin bzw. einen Schüler wählen.')); return; }
    ev.currentTarget.disabled = true;
    try {
      const r = await senden(`${BASIS}/${id}/einladungen`, mitLehrer({ schuelerIds: ids, text: seite.querySelector('[data-text]').value }));
      toast(`${r.neu} ${r.neu === 1 ? 'Einladung' : 'Einladungen'} versendet${r.ohneKonto.length ? ` – ohne Elternkonto: ${r.ohneKonto.join(', ')}` : ''}.`);
      neu();
    } catch (err) { kontext.fehler(err); ev.currentTarget.disabled = false; }
  });
  seite.querySelectorAll('[data-einladung-weg]').forEach(b => b.onclick = async () => {
    try { await api(`${BASIS}/${id}/einladungen/${b.dataset.einladungWeg}`, { methode: 'DELETE' }); toast('Einladung gelöscht.'); neu(); } catch (err) { kontext.fehler(err); }
  });

  // Sperren / Freigeben
  const sperren = async (beginne, gesperrt) => {
    try {
      await senden(`${BASIS}/${id}/sperren`, mitLehrer({ beginne, gesperrt }), 'PUT');
      toast(gesperrt ? `${beginne.length === 1 ? 'Zeitfenster' : beginne.length + ' Zeitfenster'} gesperrt.` : 'Freigegeben.');
      neu();
    } catch (err) { kontext.fehler(err); }
  };
  seite.querySelectorAll('[data-sperren]').forEach(b => b.onclick = () => sperren([b.dataset.sperren], true));
  seite.querySelectorAll('[data-freigeben]').forEach(b => b.onclick = () => sperren([b.dataset.freigeben], false));
  const auswahlNeu = () => {
    const gew = [...seite.querySelectorAll('[data-fenster]:checked')];
    gew.forEach(c => c.closest('tr').classList.add('nl-gewaehlt'));
    seite.querySelectorAll('[data-fenster]:not(:checked)').forEach(c => c.closest('tr').classList.remove('nl-gewaehlt'));
    const status = gew.map(c => c.closest('tr').className);
    seite.querySelector('[data-sammel="sperren"]').disabled = !status.some(s => s.includes('st-frei'));
    seite.querySelector('[data-sammel="freigeben"]').disabled = !status.some(s => s.includes('st-gesperrt'));
    seite.querySelector('[data-gewaehlt]').textContent = gew.length ? `${gew.length} Zeitfenster gewählt` : 'Zeitfenster wählen, um sie zu sperren (abwesend) oder freizugeben.';
  };
  seite.querySelectorAll('[data-fenster]').forEach(c => c.onchange = auswahlNeu);
  seite.querySelectorAll('[data-sammel]').forEach(b => b.onclick = () => {
    const gesperrt = b.dataset.sammel === 'sperren';
    const beginne = [...seite.querySelectorAll('[data-fenster]:checked')].filter(c => c.closest('tr').classList.contains(gesperrt ? 'st-frei' : 'st-gesperrt')).map(c => c.dataset.fenster);
    if (beginne.length) sperren(beginne, gesperrt);
  });

  // Termin eintragen / verschieben / stornieren
  const freie = (ausser = null) => d.zeitfenster.filter(z => z.status !== 'gebucht' || z.termin?.id === ausser);
  seite.querySelectorAll('[data-eintragen]').forEach(b => b.onclick = () => dialog({
    titel: `Termin eintragen · ${b.dataset.eintragen} Uhr`,
    inhalt: `<div class="row g-3">
      <div class="col-sm-5"><label class="form-label">Klasse</label><select class="form-select" data-d-klasse><option value="">alle Klassen</option>${klassen.map(k => `<option ${k === klasse ? 'selected' : ''}>${e(k)}</option>`).join('')}</select></div>
      <div class="col-sm-7"><label class="form-label">Schüler*in</label><span data-d-box></span></div>
      <div class="col-sm-5"><label class="form-label">Uhrzeit</label><select class="form-select" data-d-zeit>${freie().map(z => `<option value="${z.beginn}" ${z.beginn === b.dataset.eintragen ? 'selected' : ''} data-zweit="${z.status === 'gesperrt' ? 'gesperrt' : ''}">${z.beginn}–${z.ende}</option>`).join('')}</select></div>
      <div class="col-sm-7"><label class="form-label">Bemerkung (optional)</label><input class="form-control" data-d-bemerkung maxlength="500" placeholder="z. B. telefonisch vereinbart"></div></div>`,
    beimOeffnen: box => {
      const fuellen = () => {
        const k = box.querySelector('[data-d-klasse]').value;
        box.querySelector('[data-d-box]').innerHTML = `<select class="form-select" data-d-schueler data-platzhalter="Bitte wählen">${sch.schueler.filter(s => !k || s.klasse === k)
          .map(s => `<option value="${s.id}" data-zweit="${e(s.klasse)}">${e(schuelerName(s))}</option>`).join('')}</select>`;
      };
      box.querySelector('[data-d-klasse]').addEventListener('change', fuellen);
      fuellen();
    },
    knoepfe: [{ text: 'Abbrechen' }, { text: 'Eintragen', art: 'haupt', aktion: async box => {
      const sid = Number(box.querySelector('[data-d-schueler]').value);
      if (!sid) throw new Error('Bitte eine Schülerin bzw. einen Schüler wählen.');
      await senden(`${BASIS}/${id}/termine`, mitLehrer({ schuelerId: sid, beginn: box.querySelector('[data-d-zeit]').value, bemerkung: box.querySelector('[data-d-bemerkung]').value }));
      toast('Termin eingetragen.');
      neu();
    } }],
  }));
  seite.querySelectorAll('[data-verschieben]').forEach(b => b.onclick = () => {
    const z = d.zeitfenster.find(x => x.termin?.id === Number(b.dataset.verschieben));
    dialog({
      titel: `Termin verschieben · ${schuelerName(z.termin.schueler)}`,
      inhalt: `<div class="row g-3"><div class="col-sm-5"><label class="form-label">Neue Uhrzeit</label><select class="form-select" data-d-zeit>
          ${freie(z.termin.id).map(f => `<option value="${f.beginn}" ${f.beginn === z.beginn ? 'selected' : ''} data-zweit="${f.status === 'gesperrt' ? 'gesperrt' : ''}">${f.beginn}–${f.ende}</option>`).join('')}</select></div>
        <div class="col-sm-7"><label class="form-label">Bemerkung</label><input class="form-control" data-d-bemerkung maxlength="500" value="${e(z.termin.bemerkung)}"></div>
        ${z.termin.gebuchtVon ? '<div class="col-12 nl-unter">Die Erziehungsberechtigten werden per Mitteilung über die neue Uhrzeit informiert.</div>' : ''}</div>`,
      knoepfe: [{ text: 'Abbrechen' }, { text: 'Speichern', art: 'haupt', aktion: async box => {
        await senden(`${BASIS}/${id}/termine/${z.termin.id}`, { beginn: box.querySelector('[data-d-zeit]').value, bemerkung: box.querySelector('[data-d-bemerkung]').value }, 'PUT');
        toast('Termin gespeichert.');
        neu();
      } }],
    });
  });
  seite.querySelectorAll('[data-storno]').forEach(b => b.onclick = async () => {
    const z = d.zeitfenster.find(x => x.termin?.id === Number(b.dataset.storno));
    const grund = await absageDialog('Termin absagen', `Termin um ${z.beginn} Uhr mit ${schuelerName(z.termin.schueler)} absagen? Die Erziehungsberechtigten werden per Mitteilung informiert.`);
    if (grund === null) return;
    try { await senden(`${BASIS}/${id}/termine/${z.termin.id}/absagen`, { grund }); toast('Termin abgesagt.'); neu(); } catch (err) { kontext.fehler(err); }
  });
}

// ------------------------------------------------------------------ Erziehungsberechtigte

async function eltern(seite, kontext, id) {
  const d = await holen(`${BASIS}/${id}/eltern`);
  const t = d.sprechtag;
  const neu = () => eltern(seite, kontext, id);
  const lesen = !!d.nurLesen;
  kontext.titel(t.name, lesen ? '' : `<a class="nl-knopf" href="${BASIS}/${id}/elternplan.pdf" data-pdf="hoch" target="_blank" rel="noopener">${icon('file-earmark-pdf')}Meine Termine (PDF)</a>`);
  const termine = d.termine.slice().sort((a, b) => a.beginn.localeCompare(b.beginn));
  const voll = d.maxTermine > 0 && d.gebuchtFamilie >= d.maxTermine;
  const lehrerZeile = (k, l) => {
    const faecher = l.faecher.length ? l.faecher.join(', ') : '';
    const merkmale = [l.klassenleitung ? '<span class="nl-chip grau">Klassenleitung</span>' : '',
      l.online ? '<span class="nl-chip grau"><i class="bi bi-camera-video"></i>online</span>' : '',
      l.eingeladen ? `<span class="nl-chip lila" ${l.einladungText ? `data-hover="${e(l.einladungText)}" data-hover-art="dunkel"` : ''}><i class="bi bi-envelope-paper"></i>Einladung</span>` : ''].join('');
    let termin;
    if (l.termin) termin = `<span class="st-zeit gebucht">${e(l.termin.beginn)}</span> <span class="nl-unter">bis ${e(l.termin.ende)}</span>
        ${absagbar(t) && !lesen ? `<button type="button" class="nl-knopf rot st-klein ms-2" data-storno="${l.termin.id}" data-text="${e(`${l.termin.beginn} Uhr bei ${l.name}`)}">Absagen</button>` : ''}`;
    else if (l.abgesagt) termin = '<span class="nl-chip rot">Teilnahme abgesagt</span>';
    else if (l.warteliste) termin = `<span class="nl-chip lila">Warteliste · Platz ${l.warteliste.position}</span>${lesen ? '' : `<button type="button" class="nl-knopf st-klein ms-2" data-warte-weg="${l.warteliste.id}">Austragen</button>`}`;
    else if (lesen) termin = `<span class="nl-unter">${t.status === 'angekuendigt' ? `Buchung ab ${e(zeitDe(t.anmeldungVon))}` : d.buchbar ? '' : '–'}</span>`;
    else if (!d.buchbar) termin = `<span class="nl-unter">${t.status === 'angekuendigt' || t.phase === 'einladung' ? `Buchung ab ${e(zeitDe(t.anmeldungVon))}` : 'keine Buchung mehr möglich'}</span>`;
    else if (l.ausgebucht || !l.zeitfenster?.length) termin = d.warteliste
      ? `<span class="nl-unter">ausgebucht</span><button type="button" class="nl-knopf st-klein ms-2" data-warten data-lehrer="${e(l.kurz)}" data-kind="${k.id}">Auf die Warteliste</button>`
      : '<span class="nl-unter">keine freien Termine</span>';
    else if (voll) termin = `<span class="nl-unter">Höchstzahl von ${d.maxTermine} Terminen erreicht</span>`;
    else termin = `<span class="st-buchen"><select class="form-select form-select-sm" data-zeit aria-label="Uhrzeit bei ${e(l.name)}" data-platzhalter="Uhrzeit wählen">
        <option value="">Uhrzeit wählen</option>${l.zeitfenster.map(z => `<option value="${z.beginn}" ${z.konflikt ? 'disabled data-zweit="Überschneidung mit einem Ihrer Termine"' : ''}>${z.beginn}–${z.ende}</option>`).join('')}</select>
        <button type="button" class="nl-hinzu st-klein" data-buchen data-lehrer="${e(l.kurz)}" data-kind="${k.id}">Buchen</button></span>`;
    return `<tr><td><span class="st-lehrkraft"><span class="nl-name">${e(l.name)}</span>${merkmale}</span></td><td class="nl-unter">${e(faecher)}${l.dauer !== t.dauer ? ` · ${l.dauer} Min.` : ''}</td><td>${l.online ? '<span class="nl-unter">online</span>' : l.raum ? e(l.raum) : '<span class="nl-unter">–</span>'}</td><td>${termin}</td></tr>`;
  };
  // angekündigt: Eckdaten für die Terminplanung der Familie
  const ankuendigung = t.status === 'angekuendigt' ? `<section class="st-karte"><header><i class="bi bi-megaphone"></i><h2>Angekündigt</h2></header>
      <div class="st-ankuendigung">
        <div><span>Datum</span><b>${e([t.datumText, ...(t.reihe ?? []).filter(r => r.id !== t.id).map(r => r.datumText)].join(' · '))}</b></div>
        <div><span>Uhrzeit</span><b>${e(t.beginn)}–${e(t.ende)} Uhr</b></div>
        ${t.ort ? `<div><span>Ort</span><b>${e(t.ort)}</b></div>` : ''}
        <div><span>Anmeldung</span><b>${e(t.anmeldungVonText)} – ${e(t.anmeldungBisText)}</b></div>
      </div>
      <p class="nl-unter mt-3 mb-0">${lesen ? 'Die Termine buchen deine Erziehungsberechtigten im Anmeldezeitraum.' : 'Die Buchung ist ab Beginn der Anmeldung möglich. Unten sehen Sie bereits die beteiligten Lehrkräfte Ihres Kindes.'}</p></section>` : '';
  const optionenText = [d.maxTermine > 0 ? `höchstens ${d.maxTermine} Termine je Familie (gebucht: ${d.gebuchtFamilie})` : '', d.warteliste ? 'Warteliste bei ausgebuchten Lehrkräften' : ''].filter(Boolean).join(' · ');
  seite.innerHTML = `${kopfInfo(t, '', true)}
    ${ankuendigung}
    ${optionenText && !lesen ? `<div class="nl-hinweis"><i class="bi bi-info-circle"></i>${e(optionenText)}</div>` : ''}
    <section class="st-karte"><header><i class="bi bi-person-video3"></i><h2>${lesen ? 'Termine' : 'Meine Termine'}</h2><span class="nl-zahl gelb"><b>${termine.length}</b></span></header>
      ${termine.length ? `<div class="nl-tabelle st-flach"><table><thead><tr><th>Zeit</th><th>Lehrkraft</th><th>Ort</th><th>Kind</th><th></th></tr></thead><tbody>
        ${termine.map(x => `<tr><td class="text-nowrap"><span class="st-zeit gebucht">${e(x.beginn)}</span> <span class="nl-unter">– ${e(x.ende)}</span></td><td class="nl-name st-n">${e(x.lehrerName)}</td>
          <td>${x.online ? (x.link ? `<a class="st-link" href="${e(x.link)}" target="_blank" rel="noopener noreferrer"><i class="bi bi-camera-video"></i> Online-Gespräch</a>` : '<span class="nl-unter">online</span>') : x.raum ? e(x.raum) : '–'}</td>
          <td>${e(`${x.schueler.vorname} ${x.schueler.name}`)} <span class="nl-unter">${e(x.schueler.klasse)}</span></td>
          <td class="text-end">${absagbar(t) && !lesen ? `<button type="button" class="nl-knopf rot st-klein" data-storno="${x.id}" data-text="${e(`${x.beginn} Uhr bei ${x.lehrerName}`)}" title="Termin absagen" aria-label="Termin absagen">Absagen</button>` : ''}</td></tr>`).join('')}
        </tbody></table></div>` : `<div class="p-3 nl-unter">Noch keine Termine gebucht.</div>`}
    </section>
    ${d.kinder.length ? d.kinder.map(k => `<section class="st-karte"><header><i class="bi bi-people"></i><h2>${lesen ? 'Beteiligte Lehrkräfte' : `Lehrkräfte von ${e(`${k.vorname} ${k.name}`)}`}</h2><span class="nl-tag">${e(k.klasse)}</span></header>
      ${k.lehrkraefte.length ? `<div class="nl-tabelle st-flach"><table><thead><tr><th>Lehrkraft</th><th>Fächer</th><th>Ort</th><th>Termin</th></tr></thead><tbody>
        ${k.lehrkraefte.map(l => lehrerZeile(k, l)).join('')}</tbody></table></div>` : `<div class="p-3 nl-unter">${lesen ? 'Keine deiner Lehrkräfte nimmt an diesem Sprechtag teil.' : 'Keine Lehrkraft Ihres Kindes nimmt an diesem Sprechtag teil.'}</div>`}
    </section>`).join('') : alarm('warning', lesen ? 'Deinem Konto ist keine Schülerin bzw. kein Schüler zugeordnet.' : 'Ihrem Konto ist kein Kind zugeordnet. Bitte wenden Sie sich an die Schule.')}`;

  seite.querySelectorAll('[data-buchen]').forEach(b => b.onclick = async () => {
    const zeit = b.closest('td').querySelector('[data-zeit]').value;
    if (!zeit) { kontext.fehler(new Error('Bitte eine Uhrzeit wählen.')); return; }
    b.disabled = true;
    try {
      await senden(`${BASIS}/${id}/buchen`, { lehrer: b.dataset.lehrer, schuelerId: Number(b.dataset.kind), beginn: zeit });
      toast(`Termin um ${zeit} Uhr gebucht.`);
      neu();
    } catch (err) { kontext.fehler(err); b.disabled = false; }
  });
  seite.querySelectorAll('[data-warten]').forEach(b => b.onclick = async () => {
    b.disabled = true;
    try {
      const r = await senden(`${BASIS}/${id}/warteliste`, { lehrer: b.dataset.lehrer, schuelerId: Number(b.dataset.kind) });
      toast(`Auf der Warteliste (Platz ${r.position}). Sie erhalten eine Mitteilung, sobald ein Termin frei wird.`);
      neu();
    } catch (err) { kontext.fehler(err); b.disabled = false; }
  });
  seite.querySelectorAll('[data-warte-weg]').forEach(b => b.onclick = async () => {
    try { await api(`${BASIS}/${id}/warteliste/${b.dataset.warteWeg}`, { methode: 'DELETE' }); toast('Von der Warteliste ausgetragen.'); neu(); } catch (err) { kontext.fehler(err); }
  });
  seite.querySelectorAll('[data-storno]').forEach(b => b.onclick = async () => {
    const grund = await absageDialog('Termin absagen', `Termin ${b.dataset.text} absagen? Die Lehrkraft wird per Mitteilung informiert.`);
    if (grund === null) return;
    try { await senden(`${BASIS}/${id}/termine/${b.dataset.storno}/absagen`, { grund }); toast('Termin abgesagt.'); neu(); } catch (err) { kontext.fehler(err); }
  });
}
