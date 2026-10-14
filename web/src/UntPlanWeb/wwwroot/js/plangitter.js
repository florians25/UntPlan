// Darstellung der Stundenpläne: Raster nach Unterrichtsstunden, Stundenkarten, Veranstaltungsleiste, Detaildialog mit Klassenbuch.
import { e, minuten, jetztMinuten, heuteIso, datumKurz, datumLang, dialog, holen, senden, toast, WOCHENTAGE_KURZ } from './ui.js';

/** Höhe einer Unterrichtsstunde im Raster (Pausen werden nicht maßstäblich dargestellt). */
const ZEILE = 78;

export const STATUSTEXT = {
  geaendert: 'Änderung', offen: 'Vertretung offen', entfall: 'Entfall', abwesend: 'vertreten', klausur: 'Prüfung',
  bereitschaft: 'Bereitschaft', buchung: 'Raumbuchung', vorgemerkt: 'vorgemerkt', veranstaltung: 'Veranstaltung',
};

export function statusText(k) {
  if ((k.status === 'entfall' || k.status === 'geaendert') && k.art && k.art.length <= 18) return k.art;
  return STATUSTEXT[k.status] ?? '';
}

/** Zusätzliche Stunde (Sondereinsatz, Verlegung hierher …) – Marker „+“ statt Änderungsquadrat. */
const istZusatz = k => k.status === 'geaendert' && /sondereinsatz|zusatz|verlegung|statt|mitbetreuung/i.test(k.art || '');

/**
 * Werte (Klassen, Lehrkräfte, Räume) wie im Original: neuer Wert als grüne Kürzelbox, offene Vertretung als rote
 * Box „???“. Der ersetzte alte Wert steht im Detaildialog; bei Entfall erscheinen die ursprünglichen Werte durchgestrichen.
 */
export function werte(liste, entfall = false) {
  return (liste || []).map(w => {
    if (entfall) return e(w.alt || w.k);
    if (w.alt && !w.k) return '<span class="lbl offen">???</span>';
    if (w.alt || w.neu) return `<span class="lbl">${e(w.k)}</span>`;
    return e(w.k);
  }).join(', ');
}

function titelText(k) {
  const w = l => (l || []).map(x => x.alt && x.k ? `${x.k} (statt ${x.alt})` : x.k || '???').join(', ');
  return [k.fachName || k.fach || k.text, w(k.klassen), w(k.lehrer), w(k.raeume), k.status !== 'regulaer' ? statusText(k) : '', k.fach && k.text].filter(Boolean).join(' · ');
}

function marker(k) {
  const m = [];
  if (k.status === 'entfall' || k.status === 'abwesend') m.push('<i class="mk-dreieck"></i>');
  if (k.status === 'klausur') m.push('<i class="mk-kreis"></i>');
  if (istZusatz(k)) m.push('<i class="mk-plus"></i>');
  else if (k.status === 'geaendert' || k.status === 'offen' || k.status === 'buchung') m.push('<i class="mk-quadrat"></i>');
  return m.length ? `<span class="mks" aria-hidden="true">${m.join('')}</span>` : '';
}

/**
 * Inhalt einer Karte. Oberste Zeile wie im Original: im Klassenplan das Fach, im Lehrer- und Raumplan die Klasse(n);
 * darunter die übrigen Elemente. Ohne Fach (Veranstaltung, Klausur, Buchung) steht die Bezeichnung oben.
 */
export function karteInhalt(k, typ, kompakt = false) {
  const entfall = k.status === 'entfall' || k.status === 'abwesend';
  const fach = k.fachAlt && !entfall ? `<span class="lbl">${e(k.fach)}</span>` : e(k.fach || '');
  const ohneFach = !k.fach;
  let oben, rest;
  if (ohneFach) {
    const lehrer = k.lehrer.length > 6 ? `${k.lehrer.length} Lehrkräfte` : werte(k.lehrer, entfall);
    oben = e(k.text || k.art || '—');
    rest = [werte(k.klassen, entfall), typ !== 'lehrer' || k.lehrer.length > 1 ? lehrer : '', werte(k.raeume, entfall)];
  } else if (typ === 'klasse') {
    oben = fach;
    rest = [werte(k.lehrer, entfall), werte(k.raeume, entfall), k.klassen.length > 1 ? werte(k.klassen, entfall) : ''];
  } else {
    oben = werte(k.klassen, entfall) || fach;
    rest = [k.klassen.length ? fach : '', typ === 'raum' ? werte(k.lehrer, entfall) : werte(k.raeume, entfall)];
  }
  const zeilen = rest.filter(x => x);
  const rumpf = kompakt ? (zeilen.length ? `<div class="z2">${zeilen.join(' · ')}</div>` : '') : zeilen.map(x => `<div class="z2">${x}</div>`).join('');
  const info = k.text && !ohneFach ? '<span class="info" aria-hidden="true">i</span>' : '';
  return `${marker(k)}<div class="z1">${oben}</div>${rumpf}${info}`;
}

function vergangen(k, jetztIso, jetzt) {
  return k.datum < jetztIso || (k.datum === jetztIso && k.ende && minuten(k.ende) <= jetzt);
}

/** Spuren für gleichzeitige Karten: immer nebeneinander. */
function spuren(karten) {
  const sortiert = [...karten].sort((a, b) => a.von - b.von || b.bis - a.bis);
  const gruppen = [];
  let gruppe = null, ende = -1;
  for (const k of sortiert) {
    if (!gruppe || k.von > ende) { gruppe = []; gruppen.push(gruppe); ende = k.bis; }
    gruppe.push(k);
    ende = Math.max(ende, k.bis);
  }
  const res = new Map();
  for (const g of gruppen) {
    const enden = [];
    for (const k of g) {
      let spur = enden.findIndex(x => x < k.von);
      if (spur < 0) { spur = enden.length; enden.push(0); }
      enden[spur] = k.bis;
      res.set(k, { spur, anzahl: 0 });
    }
    for (const k of g) res.get(k).anzahl = enden.length;
  }
  return res;
}

const hhmm = m => `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`;

/** Position der Jetzt-Linie im Stundenraster (null außerhalb des Unterrichts). */
function jetztPosition(zeiten, jetzt) {
  for (let i = 0; i < zeiten.length; i++) {
    const b = minuten(zeiten[i].beginn), en = minuten(zeiten[i].ende);
    if (jetzt >= b && jetzt < en) return Math.round((i + (jetzt - b) / Math.max(1, en - b)) * ZEILE);
    if (i + 1 < zeiten.length && jetzt >= en && jetzt < minuten(zeiten[i + 1].beginn)) return (i + 1) * ZEILE;
  }
  return null;
}

const leisteEintrag = (text, art) => `<div class="tt-ereignis ${art}" title="${e(text)}">${e(text)}</div>`;

/** Wochenraster als HTML. plan: Antwort von /api/app/plan; gewaehlterTag: Index für die Mobilansicht. */
export function wochenGitter(plan, { gewaehlterTag = null } = {}) {
  const alle = plan.zeitraster.filter(z => z.beginn && z.ende);
  if (!alle.length) return '<div class="alert alert-secondary">Kein Zeitraster übertragen.</div>';
  const letzteBelegt = Math.max(6, ...plan.karten.map(k => k.bis), ...plan.aufsichten.map(a => a.nachStunde + 1),
    ...plan.tage.flatMap(t => t.ereignisse.filter(x => !x.ganztaegig).map(x => x.bis)));
  const zeiten = alle.filter(z => z.nr <= letzteBelegt);
  const index = new Map(zeiten.map((z, i) => [z.nr, i]));
  const letzte = zeiten[zeiten.length - 1].nr;
  const hoehe = zeiten.length * ZEILE;
  const heute = heuteIso();
  const jetzt = jetztMinuten();
  const tage = plan.tage;
  const ausgewaehlt = gewaehlterTag ?? Math.max(0, tage.findIndex(t => t.datum === heute));
  const jetztY = tage.some(t => t.datum === heute) ? jetztPosition(zeiten, jetzt) : null;
  const mitLeiste = tage.some(t => t.hinweise.length || t.ereignisse.some(x => x.ganztaegig && !plan.karten.some(k => k.datum === t.datum && k.text === x.titel)));

  let html = `<div class="tt" style="--tage:${tage.length}"><div class="tt-ecke"></div>`;
  tage.forEach((t, i) => {
    html += `<div class="tt-tag ${t.datum === heute ? 'heute' : ''} ${i === ausgewaehlt ? 'gewaehlt' : ''}">${e(WOCHENTAGE_KURZ[i] ?? t.wochentag)} ${datumKurz(t.datum)}</div>`;
  });
  if (mitLeiste) {
    html += '<div class="tt-ecke"></div>';
    tage.forEach((t, i) => {
      html += `<div class="tt-leiste ${i === ausgewaehlt ? 'gewaehlt' : ''}">` +
        t.ereignisse.filter(x => x.ganztaegig && !plan.karten.some(k => k.datum === t.datum && k.text === x.titel)).map(x => leisteEintrag(x.titel, 'veranstaltung')).join('') +
        t.hinweise.map(h => leisteEintrag(h, 'hinweis')).join('') + '</div>';
    });
  }

  // Zeitachse: Beginn jeder Stunde, nach einer Pause zusätzlich das Ende der vorherigen Stunde
  html += `<div class="tt-achse" style="height:${hoehe}px">`;
  zeiten.forEach((z, i) => {
    const vorher = zeiten[i - 1];
    html += `<div class="tt-zeit" style="top:${i * ZEILE}px">${vorher && vorher.ende !== z.beginn ? `${e(vorher.ende)}<br>` : ''}${e(z.beginn)}<small>${e(z.bezeichnung)}.</small></div>`;
  });
  html += `<div class="tt-zeit" style="top:${hoehe}px">${e(zeiten[zeiten.length - 1].ende)}</div>`;
  if (jetztY !== null) html += `<div class="tt-jetzt-zeit" style="top:${jetztY}px">${hhmm(jetzt)}</div>`;
  html += '</div>';

  tage.forEach((t, i) => {
    html += `<div class="tt-spalte ${t.frei ? 'frei' : ''} ${i === ausgewaehlt ? 'gewaehlt' : ''}" style="height:${hoehe}px">`;
    html += zeiten.map((z, j) => `<div class="tt-linie" style="top:${j * ZEILE}px"></div>`).join('');
    if (t.frei) html += `<div class="tt-frei">${e(t.freiGrund || 'unterrichtsfrei')}</div>`;
    const karten = plan.karten.filter(k => k.datum === t.datum && index.has(k.von));
    // Veranstaltungen mit Stunden, die nicht schon als Karte (Teilnahme) im Plan stehen
    const ereignisKarten = t.ereignisse.filter(x => !x.ganztaegig && index.has(x.von) && !karten.some(k => k.text === x.titel && k.von <= x.bis && k.bis >= x.von))
      .map(x => ({ ereignis: true, datum: t.datum, von: x.von, bis: Math.min(x.bis, letzte), status: 'veranstaltung', fach: '', text: x.titel, art: 'Veranstaltung',
        klassen: x.klassen.map(k => ({ k })), lehrer: x.lehrer.map(k => ({ k })), raeume: x.raeume.map(k => ({ k })) }));
    const alleKarten = [...karten, ...ereignisKarten];
    const lage = spuren(alleKarten);
    for (const k of alleKarten) {
      const { spur, anzahl } = lage.get(k);
      const breite = 100 / anzahl;
      const von = index.get(k.von), bis = index.get(Math.min(k.bis, letzte)) ?? von;
      const oben = von * ZEILE + 1, h = (bis - von + 1) * ZEILE - 2;
      const nr = k.ereignis ? '' : `data-karte="${plan.karten.indexOf(k)}"`;
      html += `<div class="std ${e(k.status)} ${vergangen(k, heute, jetzt) && k.status === 'regulaer' ? 'vergangen' : ''}" ${nr} tabindex="0" role="button"
        title="${e(titelText(k))}" style="top:${oben}px;height:${h}px;left:calc(${spur * breite}% + 1px);width:calc(${breite}% - 2px)">${karteInhalt(k, plan.typ, h < 60)}</div>`;
    }
    for (const a of plan.aufsichten.filter(a => a.datum === t.datum && index.has(a.nachStunde))) {
      const y = (index.get(a.nachStunde) + 1) * ZEILE;
      const text = a.status === 'vertreten' ? `Aufsicht ${a.gang} → ${a.lehrer}` : a.status === 'vertretung' ? `Aufsicht ${a.gang} (für ${a.alt})` : `Aufsicht ${a.gang}`;
      html += `<div class="aufsicht ${e(a.status)}" style="top:${y - 11}px" title="${e(a.gangName)} ${e(a.beginn)}–${e(a.ende)}">${e(text)}</div>`;
    }
    if (jetztY !== null) html += `<div class="tt-jetzt" style="top:${jetztY}px"></div>`;
    html += '</div>';
  });
  return html + '</div>';
}

export function tagAuswahl(plan, gewaehlt) {
  return `<div class="tt-tagwahl btn-group w-100 mb-2" role="group">${plan.tage.map((t, i) =>
    `<button type="button" data-tagwahl="${i}" class="btn btn-sm ${i === gewaehlt ? 'btn-primary' : 'btn-outline-secondary'}">${WOCHENTAGE_KURZ[i]}<br><small>${datumKurz(t.datum)}</small></button>`).join('')}</div>`;
}

/** Schwebende Legende wie im Original. */
export function legende() {
  return `<div class="tt-legende-huelle nicht-drucken"><div class="tt-legende">
    <span><i class="mk-dreieck"></i>Entfall</span><span><i class="mk-kreis"></i>Prüfung</span><span><i class="mk-quadrat"></i>Änderung</span>
  </div></div>`;
}

/** Klick auf eine Karte öffnet den Detaildialog. */
export function kartenBinden(wurzel, karten, kontext) {
  const oeffnen = el => { const k = karten[Number(el.dataset.karte)]; if (k) karteDialog(k, kontext); };
  wurzel.querySelectorAll('[data-karte]').forEach(el => {
    el.addEventListener('click', () => oeffnen(el));
    el.addEventListener('keydown', ev => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); oeffnen(el); } });
  });
}

function zeileWert(titel, liste) {
  if (!liste?.length) return '';
  const html = liste.map(w => w.alt && !w.k ? `<span class="alt">${e(w.alt)}</span><span class="text-danger fw-semibold">offen</span>`
    : w.alt ? `<span class="alt">${e(w.alt)}</span><span class="fw-semibold text-success">${e(w.k)}</span>` : e(w.k)).join(', ');
  return `<tr><th class="text-body-secondary fw-normal" style="width:35%">${titel}</th><td>${html}</td></tr>`;
}

const BADGE = { entfall: 'text-bg-danger', abwesend: 'text-bg-danger', offen: 'text-bg-danger', klausur: 'text-bg-warning', bereitschaft: 'text-bg-secondary', vorgemerkt: 'text-bg-light border', veranstaltung: 'text-bg-secondary' };

/** Detaildialog einer Stunde; die unterrichtende Lehrkraft kann den Klassenbucheintrag bearbeiten. */
export async function karteDialog(k, kontext) {
  const zeit = `${datumLang(k.datum)} · ${k.von === k.bis ? k.von + '. Stunde' : k.von + '.–' + k.bis + '. Stunde'}${k.beginn ? ` · ${k.beginn}–${k.ende}` : ''}`;
  const st = statusText(k);
  const inhalt = `<p class="text-body-secondary small mb-2">${e(zeit)}</p>
    ${st && k.status !== 'regulaer' ? `<p class="mb-2"><span class="badge ${BADGE[k.status] ?? 'text-bg-success'}">${e(st)}</span></p>` : ''}
    <table class="table table-sm mb-0"><tbody>
      ${k.fach ? `<tr><th class="text-body-secondary fw-normal" style="width:35%">Fach</th><td>${k.fachAlt ? `<span class="alt">${e(k.fachAlt)}</span>` : ''}${e(k.fachName || k.fach)}${k.fachName && k.fachName !== k.fach ? ` (${e(k.fach)})` : ''}</td></tr>` : ''}
      ${zeileWert('Klasse(n)', k.klassen)}${zeileWert(k.lehrer.length > 1 ? 'Lehrkräfte' : 'Lehrkraft', k.lehrer)}${zeileWert('Raum', k.raeume)}
      ${k.art ? `<tr><th class="text-body-secondary fw-normal">Art</th><td>${e(k.art)}</td></tr>` : ''}
      ${k.text ? `<tr><th class="text-body-secondary fw-normal">${k.fach ? 'Info' : 'Bezeichnung'}</th><td>${e(k.text)}</td></tr>` : ''}
    </tbody></table>
    <div data-klassenbuch></div>`;
  const box = dialog({ titel: `${k.fach || k.text || k.art || 'Stunde'}${k.klassen.length && k.klassen.length < 6 ? ' · ' + k.klassen.map(x => x.k).join(', ') : ''}`, inhalt });
  const ziel = box.querySelector('[data-klassenbuch]');
  if (!k.unterricht || ['entfall', 'vorgemerkt', 'buchung', 'veranstaltung'].includes(k.status)) return;
  try {
    const r = await holen(`/api/app/klassenbuch/stunde?datum=${k.datum}&stunde=${k.von}&unterricht=${k.unterricht}`);
    const eintrag = r.eintrag;
    if (r.darf) {
      ziel.innerHTML = `<h6 class="mt-4 mb-3">Klassenbuch</h6>
        <div class="mb-3"><label class="form-label small">Unterrichtsinhalt</label><textarea class="form-control" name="thema" rows="2">${e(eintrag?.thema)}</textarea></div>
        <div class="row g-3 mb-3">
          <div class="col-sm-8"><label class="form-label small">Hausaufgabe</label><textarea class="form-control" name="hausaufgabe" rows="2">${e(eintrag?.hausaufgabe)}</textarea></div>
          <div class="col-sm-4"><label class="form-label small">fällig am</label><input type="date" class="form-control" name="faellig" value="${e(eintrag?.faellig ?? '')}"></div>
        </div>
        <div class="mb-3"><label class="form-label small">Notiz (nur Kollegium)</label><input class="form-control" name="notiz" value="${e(eintrag?.notiz)}"></div>
        <div class="d-flex align-items-center gap-2"><button class="btn btn-primary" data-speichern>Speichern</button>
          ${eintrag ? `<span class="text-body-secondary small">zuletzt geändert ${e(eintrag.geaendert)}</span>` : ''}</div>`;
      ziel.querySelector('[data-speichern]').onclick = async () => {
        const f = n => ziel.querySelector(`[name=${n}]`).value;
        try {
          await senden('/api/app/klassenbuch/stunde', { datum: k.datum, stunde: k.von, unterricht: k.unterricht, thema: f('thema'), hausaufgabe: f('hausaufgabe'), faellig: f('faellig') || null, notiz: f('notiz') }, 'PUT');
          toast('Klassenbucheintrag gespeichert.');
          box.schliessen();
          kontext?.nachSpeichern?.();
        } catch (err) { toast(err.message, true); }
      };
    } else if (eintrag && (eintrag.thema || eintrag.hausaufgabe)) {
      ziel.innerHTML = `<h6 class="mt-4 mb-2">Klassenbuch</h6><table class="table table-sm mb-0"><tbody>
        ${eintrag.thema ? `<tr><th class="text-body-secondary fw-normal" style="width:35%">Inhalt</th><td>${e(eintrag.thema)}</td></tr>` : ''}
        ${eintrag.hausaufgabe ? `<tr><th class="text-body-secondary fw-normal">Hausaufgabe</th><td>${e(eintrag.hausaufgabe)}${eintrag.faellig ? ` <span class="badge text-bg-light border">bis ${datumKurz(eintrag.faellig)}</span>` : ''}</td></tr>` : ''}
        </tbody></table>`;
    }
  } catch { /* Klassenbuch optional */ }
}

/** Tagesübersicht (Elemente × Stunden). */
export function tagesGitter(tag) {
  const zeiten = tag.zeitraster;
  const max = Math.max(6, ...tag.zeilen.flatMap(z => z.karten.map(k => k.bis)));
  const spalten = zeiten.filter(z => z.nr <= max);
  let html = `<div class="tagplan"><div class="tagplan-gitter" style="grid-template-columns:130px repeat(${spalten.length}, minmax(104px, 1fr))">`;
  html += '<div class="tagplan-kopf" style="border-left:0"></div>' + spalten.map(z => `<div class="tagplan-kopf"><b>${e(z.bezeichnung)}.</b>${e(z.beginn)}–${e(z.ende)}</div>`).join('');
  const alle = [];
  const jetztIso = heuteIso(), jetzt = jetztMinuten();
  for (const zeile of tag.zeilen) {
    html += `<div class="tagplan-element">${e(zeile.kurz)}<small>${e(zeile.name !== zeile.kurz ? zeile.name : '')}</small></div>`;
    const belegt = new Set();
    const sortiert = [...zeile.karten].sort((a, b) => a.von - b.von);
    for (const z of spalten) {
      if (belegt.has(z.nr)) continue;
      const hier = sortiert.filter(k => k.von === z.nr);
      if (!hier.length) { html += '<div class="tagplan-zelle"></div>'; continue; }
      const bis = Math.min(max, Math.max(...hier.map(k => k.bis)));
      for (let n = z.nr + 1; n <= bis; n++) belegt.add(n);
      html += `<div class="tagplan-zelle" style="grid-column:span ${bis - z.nr + 1}">` + hier.map(k => {
        alle.push(k);
        return `<div class="std ${e(k.status)} ${vergangen(k, jetztIso, jetzt) && k.status === 'regulaer' ? 'vergangen' : ''}" data-karte="${alle.length - 1}" tabindex="0" role="button" title="${e(titelText(k))}">${karteInhalt(k, tag.typ)}</div>`;
      }).join('') + '</div>';
    }
  }
  html += '</div></div>';
  return { html, karten: alle };
}

/** Liste eines Tages (Übersicht) mit denselben Karten, Veranstaltungen und Pausenaufsichten. */
export function tagesListe(karten, aufsichten, typ, ereignisse = []) {
  const jetztIso = heuteIso(), jetzt = jetztMinuten();
  const eintraege = [
    ...ereignisse.filter(x => x.ganztaegig).map(x => ({ zeit: -1, html: `<div class="eintrag"><div class="zeit">ganztägig</div><div class="tt-ereignis veranstaltung flex-grow-1">${e(x.titel)}</div></div>` })),
    ...karten.map((k, i) => ({ zeit: minuten(k.beginn), html: `<div class="eintrag">
        <div class="zeit"><b>${k.von === k.bis ? k.von + '.' : k.von + '.–' + k.bis + '.'}</b>${e(k.beginn)}–${e(k.ende)}</div>
        <div class="std ${e(k.status)} ${vergangen(k, jetztIso, jetzt) && k.status === 'regulaer' ? 'vergangen' : ''}" data-karte="${i}" tabindex="0" role="button" title="${e(titelText(k))}">${karteInhalt(k, typ, true)}${k.status !== 'regulaer' && statusText(k) ? `<div class="z3">${e(statusText(k))}</div>` : ''}</div></div>` })),
    ...aufsichten.map(a => ({ zeit: minuten(a.beginn), html: `<div class="eintrag"><div class="zeit">${e(a.beginn)}–${e(a.ende)}</div>
        <div class="aufsicht ${e(a.status)}">Pausenaufsicht ${e(a.gangName)}${a.status === 'vertreten' ? ' – übernimmt ' + e(a.lehrer) : a.status === 'vertretung' ? ' – Vertretung für ' + e(a.alt) : ''}</div></div>` })),
  ].sort((a, b) => a.zeit - b.zeit);
  return `<div class="tt-liste">${eintraege.map(x => x.html).join('')}</div>`;
}
