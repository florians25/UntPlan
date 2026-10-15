// Darstellung der Stundenpläne: Raster nach Unterrichtsstunden, Stundenkarten, Veranstaltungsleiste, Detaildialog mit Klassenbuch.
import { e, minuten, jetztMinuten, heuteIso, datumKurz, WOCHENTAGE_KURZ } from './ui.js';

/** Höhe einer Unterrichtsstunde im Raster (Pausen werden nicht maßstäblich dargestellt). */
const ZEILE = 80;

export const STATUSTEXT = {
  geaendert: 'Änderung', offen: 'Vertretung offen', entfall: 'Entfall', abwesend: 'vertreten', klausur: 'Prüfung',
  bereitschaft: 'Bereitschaft', buchung: 'Raumbuchung', vorgemerkt: 'vorgemerkt', veranstaltung: 'Veranstaltung',
};

export function statusText(k) {
  if ((k.status === 'entfall' || k.status === 'geaendert') && k.art && k.art.length <= 18) return k.art;
  return STATUSTEXT[k.status] ?? '';
}

/** Zusätzliche Stunde (Sondereinsatz, Verlegung hierher …) – Marker „+“ statt Änderungsquadrat. */
const istZusatz = k => k.status === 'veranstaltung' || (k.status === 'geaendert' && /sondereinsatz|zusatz|verlegung|statt|mitbetreuung/i.test(k.art || ''));

/**
 * Werte (Klassen, Lehrkräfte, Räume) wie im Original: neuer Wert als grüne Kürzelbox, bei offener Vertretung der
 * bisherige Wert durchgestrichen in der grünen Box. Der ersetzte alte Wert steht im Detaildialog; bei Entfall erscheinen die ursprünglichen Werte durchgestrichen.
 */
/** Lange Listen kürzen wie im Original: „BEC, BRA, FIS +12“ (vollständig in den Stundendetails). */
export function gekuerzt(liste, max = 3, entfall = false) {
  const l = liste || [];
  return l.length <= max + 1 ? werte(l, entfall) : `${werte(l.slice(0, max), entfall)} <span class="mehr">+${l.length - max}</span>`;
}

export function werte(liste, entfall = false) {
  return (liste || []).map(w => {
    if (entfall) return e(w.alt || w.k);
    if (w.alt && !w.k) return `<span class="lbl weg">${e(w.alt)}</span>`;
    if (w.alt || w.neu) return `<span class="lbl">${e(w.k)}</span>`;
    return e(w.k);
  }).join(', ');
}

function titelText(k) {
  const w = l => (l || []).map(x => x.alt && x.k ? `${x.k} (statt ${x.alt})` : x.k || '???').join(', ');
  return [k.fachName || k.fach || k.text, w(k.klassen), w(k.lehrer), w(k.raeume), k.status !== 'regulaer' ? statusText(k) : '', k.fach && k.text].filter(Boolean).join(' · ');
}

/** Eigene Stunde der gerade angezeigten Lehrkraft wird von anderen gehalten (Lehrkraft abwesend): gestrichelte Karte statt Entfall. */
export const lehrerAbwesend = (k, typ) => k.status === 'abwesend' && typ === 'lehrer';
/** Echter Entfall (rot, durchgestrichen); im Raumplan gilt eine verlegte Stunde weiter als Entfall. */
const istEntfall = (k, typ) => k.status === 'entfall' || (k.status === 'abwesend' && typ !== 'lehrer');

function marker(k, typ) {
  const m = [];
  if (istEntfall(k, typ)) m.push('<i class="mk-dreieck"></i>');
  if (k.status === 'klausur') m.push('<i class="mk-kreis"></i>');
  if (istZusatz(k)) m.push('<i class="mk-plus"></i>');
  else if (k.status === 'geaendert' || k.status === 'offen' || k.status === 'buchung') m.push('<i class="mk-quadrat"></i>');
  return m.length ? `<span class="mks" aria-hidden="true">${m.join('')}</span>` : '';
}

/** Text für das Info-Symbol: Vertretungstext und Notizen (wie im Original unten rechts auf der Karte). */
export function infoText(k) {
  const z = [];
  if (k.text && k.fach && !/^entf(ä|ae|a)llt\.?$/i.test(k.text.trim())) z.push(`Vertretungstext: ${k.text}`);
  if (k.notizLehrer) z.push(`Notizen für Lehrkräfte: ${k.notizLehrer}`);
  if (k.notizSchueler) z.push(`Notizen für Schüler*innen: ${k.notizSchueler}`);
  return z.join('\n');
}

/** Symbole unten rechts: fällige Hausaufgabe und Info (Original-Icons). */
function eckSymbole(k) {
  const info = infoText(k);
  const hw = k.hausaufgabeFaellig ? '<img class="ecke-hw" src="img/wu/exact/tt-homework-icon.svg" alt="" title="Hausaufgabe fällig">' : '';
  const i = info ? `<img class="ecke-info" src="img/wu/exact/info.svg" alt="" title="${e(info)}">` : '';
  return hw || i ? `<span class="ecke" aria-hidden="true">${hw}${i}</span>` : '';
}

/**
 * Inhalt einer Karte wie im Original: im Klassenplan Lehrkraft (fett), Fach, Raum; im Lehrerplan Klasse(n) (fett), Fach, Raum;
 * im Raumplan Klasse(n), Lehrkraft, Fach. Bei zusammengefassten Kopplungen (gleiches Fach und gleicher Raum) stehen alle
 * Lehrkräfte und Klassen auf der Karte. Ohne Fach (Veranstaltung, Klausur, Buchung) steht die Bezeichnung oben.
 */
export function karteInhalt(k, typ, kompakt = false) {
  const entfall = istEntfall(k, typ);
  const fach = k.fachAlt && !entfall ? `<span class="lbl">${e(k.fach)}</span>` : e(k.fach || '');
  const ohneFach = !k.fach;
  let oben, rest;
  if (ohneFach) {
    // Veranstaltung, Klausur, Buchung: Bezeichnung, dann Klassen, Lehrkräfte (gekürzt) und Raum
    oben = e(k.text || k.art || '—');
    rest = [gekuerzt(k.klassen, 4, entfall), typ !== 'lehrer' || k.lehrer.length > 1 ? gekuerzt(k.lehrer, 3, entfall) : '', werte(k.raeume, entfall)];
  } else if (typ === 'klasse') {
    const lehrer = werte(k.lehrer, entfall);
    oben = lehrer || fach;
    rest = [lehrer ? fach : '', werte(k.raeume, entfall), k.klassen.length > 1 ? werte(k.klassen, entfall) : ''];
  } else if (typ === 'raum') {
    oben = werte(k.klassen, entfall) || fach;
    rest = [werte(k.lehrer, entfall), k.klassen.length ? fach : ''];
  } else {
    oben = werte(k.klassen, entfall) || fach;
    // Lehrkraft abwesend: eigenes Kürzel durchgestrichen als Plakette; Kopplung: alle Lehrkräfte
    const lehrerZeile = lehrerAbwesend(k, typ) ? (k.lehrer.filter(w => (w.alt || w.k)).map(w => `<span class="lbl weg">${e(w.alt || w.k)}</span>`).join(' ') || '')
      : k.lehrer.length > 1 || k.lehrer.some(w => w.alt || w.neu) ? werte(k.lehrer, entfall) : '';
    rest = [k.klassen.length ? fach : '', werte(k.raeume, entfall), lehrerZeile];
  }
  const zeilen = rest.filter(x => x);
  const rumpf = kompakt ? (zeilen.length ? `<div class="z2">${zeilen.join(' · ')}</div>` : '') : zeilen.map(x => `<div class="z2">${x}</div>`).join('');
  return `${marker(k, typ)}<div class="z1">${oben}</div>${rumpf}${eckSymbole(k)}`;
}

/** CSS-Klassen einer Karte (Status, Lehrkraft abwesend, vergangen). */
export function kartenKlasse(k, typ, jetztIso, jetzt) {
  return [k.status, lehrerAbwesend(k, typ) ? 'lehrer-abwesend' : '', istEntfall(k, typ) && k.status === 'abwesend' ? 'entfall' : '',
    vergangen(k, jetztIso, jetzt) && k.status === 'regulaer' ? 'vergangen' : ''].filter(Boolean).join(' ');
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

/** Fachfarbe als CSS-Variable für den Akzentbalken (nur gültige #RRGGBB-Werte). */
export const fachStil = k => /^#[0-9a-f]{6}$/i.test(k.farbe || '') ? `--fach:${k.farbe};` : '';

const hhmm = m => `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`;

/** Höhe einer Pausenzeile mit Aufsichten. */
const PAUSE = 30;

/**
 * Zeilenaufbau: je Stunde eine Zeile, dazu eine Pausenzeile nur dort, wo im Zeitraster wirklich eine Pause liegt
 * (Ende ≠ nächster Beginn) und in dieser Woche eine Pausenaufsicht stattfindet. Liefert die Oberkante jeder Stunde und jeder Pausenzeile.
 */
function zeilenAufbau(zeiten, aufsichten) {
  const pausenNach = new Set(aufsichten.map(a => a.nachStunde));
  const oben = [], pause = new Map();
  let y = 0;
  zeiten.forEach((z, i) => {
    const vorher = i === 0 ? z.nr - 1 : zeiten[i - 1].nr;
    const luecke = i === 0 || zeiten[i - 1].ende !== z.beginn;
    if (luecke && pausenNach.has(vorher)) { pause.set(vorher, y); y += PAUSE; }
    oben.push(y);
    y += ZEILE;
  });
  const letzte = zeiten[zeiten.length - 1].nr;
  if (pausenNach.has(letzte)) { pause.set(letzte, y); y += PAUSE; }
  return { oben, pause, hoehe: y };
}

/** Position der Jetzt-Linie (null außerhalb des Unterrichts). */
function jetztPosition(zeiten, aufbau, jetzt) {
  for (let i = 0; i < zeiten.length; i++) {
    const b = minuten(zeiten[i].beginn), en = minuten(zeiten[i].ende);
    if (jetzt >= b && jetzt < en) return Math.round(aufbau.oben[i] + (jetzt - b) / Math.max(1, en - b) * ZEILE);
    if (i + 1 < zeiten.length && jetzt >= en && jetzt < minuten(zeiten[i + 1].beginn)) {
      const p = aufbau.pause.get(zeiten[i].nr);
      return p === undefined ? aufbau.oben[i + 1] : Math.round(p + (jetzt - en) / Math.max(1, minuten(zeiten[i + 1].beginn) - en) * PAUSE);
    }
  }
  return null;
}

/** Block an Pausenzeilen teilen: Aufsichten trennen Doppelstunden. */
function abschnitte(k, aufbau, index) {
  const res = [];
  let start = k.von;
  for (let n = k.von; n < k.bis; n++) if (aufbau.pause.has(n) && index.has(n + 1)) { res.push([start, n]); start = n + 1; }
  res.push([start, k.bis]);
  return res;
}

const FREI = { ferien: 'Ferien', feiertag: 'Feiertag', frei: 'Unterrichtsfrei' };
const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';

/** Ferien, Feiertage und freie Tage wie im Original: hellblaue Spalte, mittig Schloss + Art, Name und Zeitraum. */
function freiUeberlagerung(t) {
  const titel = FREI[t.freiArt];
  if (!titel) return t.freiGrund ? `<div class="tt-frei">${e(t.freiGrund)}</div>` : '';
  const name = t.freiGrund && t.freiGrund.toLowerCase() !== titel.toLowerCase() ? t.freiGrund : '';
  const zeitraum = t.freiVon ? (t.freiBis && t.freiBis !== t.freiVon ? `${datumDe(t.freiVon)} - ${datumDe(t.freiBis)}` : datumDe(t.freiVon)) : '';
  return `<div class="tt-ferien"><div class="inhalt"><div class="art"><i class="bi bi-lock-fill" aria-hidden="true"></i> ${titel}</div>${name ? `<div>${e(name)}</div>` : ''}${zeitraum ? `<div>${zeitraum}</div>` : ''}</div></div>`;
}

const leisteEintrag = (text, art) => `<div class="tt-ereignis ${art}" title="${e(text)}">${e(text)}</div>`;

/** Wochentag und Datum wie im Original: „Mo 12.10.“ */
export const tagKopf = (i, iso) => { const [, m, t] = iso.split('-'); return `${WOCHENTAGE_KURZ[i] ?? ''} ${t}.${m}.`; };

/** Wochenraster als HTML. plan: Antwort von /api/app/plan; gewaehlterTag: Index für die Mobilansicht. */
export function wochenGitter(plan, { gewaehlterTag = null, kartenZeiten = false } = {}) {
  const alle = plan.zeitraster.filter(z => z.beginn && z.ende);
  if (!alle.length) return '<div class="alert alert-secondary">Kein Zeitraster übertragen.</div>';
  // Raster immer vollständig (alle Stunden des Zeitrasters), wie im Original
  const zeiten = alle;
  const index = new Map(zeiten.map((z, i) => [z.nr, i]));
  const letzte = zeiten[zeiten.length - 1].nr;
  const aufbau = zeilenAufbau(zeiten, plan.aufsichten);
  const heute = heuteIso();
  const jetzt = jetztMinuten();
  const tage = plan.tage;
  const ausgewaehlt = gewaehlterTag ?? Math.max(0, tage.findIndex(t => t.datum === heute));
  const jetztY = tage.some(t => t.datum === heute) ? jetztPosition(zeiten, aufbau, jetzt) : null;
  const mitLeiste = tage.some(t => t.hinweise.length);

  let html = `<div class="tt" style="--tage:${tage.length}"><div class="tt-ecke"></div>`;
  tage.forEach((t, i) => {
    html += `<div class="tt-tag ${t.datum === heute ? 'heute' : ''} ${i === ausgewaehlt ? 'gewaehlt' : ''}">${e(tagKopf(i, t.datum))}</div>`;
  });
  if (mitLeiste) {
    html += '<div class="tt-ecke"></div>';
    tage.forEach((t, i) => { html += `<div class="tt-leiste ${i === ausgewaehlt ? 'gewaehlt' : ''}">${t.hinweise.map(h => leisteEintrag(h, 'hinweis')).join('')}</div>`; });
  }

  // Zeitachse wie im Original: Beginn oben an der Stunde, Ende unten (über der Linie), dazwischen die Stundennummer
  html += `<div class="tt-achse" style="height:${aufbau.hoehe}px">`;
  zeiten.forEach((z, i) => {
    const oben = aufbau.oben[i];
    html += `<div class="tt-zeit beginn" style="top:${oben}px">${e(z.beginn)}</div>`;
    html += `<div class="tt-nr" style="top:${oben}px">${e(z.bezeichnung || z.nr)}.</div>`;
    html += `<div class="tt-zeit ende" style="top:${oben + ZEILE}px">${e(z.ende)}</div>`;
  });
  if (jetztY !== null) html += `<div class="tt-jetzt-zeit" style="top:${jetztY}px">${hhmm(jetzt)}</div>`;
  html += '</div>';

  const linien = new Set();
  zeiten.forEach((z, j) => { linien.add(aufbau.oben[j]); linien.add(aufbau.oben[j] + ZEILE); });
  tage.forEach((t, i) => {
    html += `<div class="tt-spalte ${t.frei ? 'frei ' + e(t.freiArt || '') : ''} ${i === ausgewaehlt ? 'gewaehlt' : ''}" style="height:${aufbau.hoehe}px">`;
    html += [...linien].map(y => `<div class="tt-linie" style="top:${y}px"></div>`).join('');
    html += [...aufbau.pause.values()].map(y => `<div class="tt-pause" style="top:${y}px;height:${PAUSE}px"></div>`).join('');
    if (t.frei) html += freiUeberlagerung(t);
    const karten = plan.karten.filter(k => k.datum === t.datum && index.has(k.von));
    // Veranstaltungen, die nicht schon als Karte (Teilnahme) im Plan stehen – als grüne Zusatzkarte
    const ereignisKarten = t.ereignisse.filter(x => index.has(Math.max(x.von, zeiten[0].nr)) && !karten.some(k => k.text === x.titel && k.von <= x.bis && k.bis >= x.von))
      .map(x => ({ ereignis: true, datum: t.datum, von: Math.max(x.von, zeiten[0].nr), bis: Math.min(x.bis, letzte), status: 'veranstaltung', fach: '', text: x.titel, art: 'Veranstaltung',
        klassen: x.klassen.map(k => ({ k })), lehrer: x.lehrer.map(k => ({ k })), raeume: x.raeume.map(k => ({ k })) }));
    const alleKarten = [...karten, ...ereignisKarten];
    const lage = spuren(alleKarten);
    for (const k of alleKarten) {
      const { spur, anzahl } = lage.get(k);
      const breite = 100 / anzahl;
      const nr = k.ereignis ? '' : `data-karte="${plan.karten.indexOf(k)}"`;
      for (const [von, bis] of abschnitte(k, aufbau, index)) {
        const iv = index.get(von), ib = index.get(Math.min(bis, letzte)) ?? iv;
        const oben = aufbau.oben[iv] + 1, h = aufbau.oben[ib] + ZEILE - aufbau.oben[iv] - 1;
        // Beginn und Ende links in der Karte (Einstellung „Uhrzeiten auf den Karten“)
        const mitZeit = kartenZeiten && h >= 60 && anzahl === 1;
        const zeit = mitZeit ? `<span class="zt o">${e(zeiten[iv].beginn)}</span><span class="zt u">${e(zeiten[ib].ende)}</span>` : '';
        html += `<div class="std ${e(kartenKlasse(k, plan.typ, heute, jetzt))} ${mitZeit ? 'mit-zeit' : ''}" ${nr} tabindex="0" role="button"
          title="${e(titelText(k))}" style="${fachStil(k)}top:${oben}px;height:${h}px;left:calc(${spur * breite}% + ${spur ? 2 : 0}px);width:calc(${breite}% - ${anzahl > 1 ? 2 : 0}px)">${zeit}${karteInhalt(k, plan.typ, h < 60)}</div>`;
      }
    }
    // Pausenaufsichten in der Pausenzeile
    const aufsichten = plan.aufsichten.filter(a => a.datum === t.datum && aufbau.pause.has(a.nachStunde));
    const proPause = new Map();
    for (const a of aufsichten) proPause.set(a.nachStunde, [...(proPause.get(a.nachStunde) ?? []), a]);
    for (const [nach, liste] of proPause) {
      const breite = 100 / liste.length;
      liste.forEach((a, j) => {
        const ersatz = a.lehrer && !/^[?+]+$/.test(a.lehrer) ? a.lehrer : '';
        const text = a.status === 'vertreten' ? (ersatz ? `${a.gangName || a.gang} → ${ersatz}` : (a.gangName || a.gang)) : a.status === 'vertretung' ? `${a.gangName || a.gang} (für ${a.alt})` : (a.gangName || a.gang);
        html += `<div class="aufsicht ${e(a.status)}" style="top:${aufbau.pause.get(nach) + 2}px;height:${PAUSE - 4}px;left:calc(${j * breite}% + ${j ? 2 : 0}px);width:calc(${breite}% - ${liste.length > 1 ? 2 : 0}px)"
          title="Pausenaufsicht ${e(a.gangName || a.gang)} ${e(a.beginn)}–${e(a.ende)}">${a.status === 'vertretung' ? '<i class="mk-plus"></i>' : ''}<span>Aufsicht ${e(text)}</span></div>`;
      });
    }
    if (jetztY !== null) html += `<div class="tt-jetzt" style="top:${jetztY}px"></div>${t.datum === heute ? `<div class="tt-jetzt heute" style="top:${jetztY}px"></div>` : ''}`;
    html += '</div>';
  });
  return html + '</div>';
}

export function tagAuswahl(plan, gewaehlt) {
  return `<div class="tt-tagwahl nicht-drucken" role="group" aria-label="Tag wählen">${plan.tage.map((t, i) =>
    `<button type="button" data-tagwahl="${i}" class="${i === gewaehlt ? 'aktiv' : ''}" aria-pressed="${i === gewaehlt}">${WOCHENTAGE_KURZ[i]}<small>${tagKopf(i, t.datum).split(' ')[1]}</small></button>`).join('')}</div>`;
}

/** Klick auf eine Karte öffnet den Detaildialog. */
export function kartenBinden(wurzel, karten, kontext, typ = null) {
  const oeffnen = el => { const k = karten[Number(el.dataset.karte)]; if (k) karteDialog(k, { ...kontext, liste: karten, typ }); };
  wurzel.querySelectorAll('[data-karte]').forEach(el => {
    el.addEventListener('click', () => oeffnen(el));
    el.addEventListener('keydown', ev => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); oeffnen(el); } });
  });
}

/** Detailansicht einer Stunde (Aufbau wie im Original) – siehe stundendetails.js. */
export { karteDialog } from './stundendetails.js';
import { karteDialog } from './stundendetails.js';

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
        return `<div class="std ${e(kartenKlasse(k, tag.typ, jetztIso, jetzt))}" data-karte="${alle.length - 1}" tabindex="0" role="button" title="${e(titelText(k))}" style="${fachStil(k)}">${karteInhalt(k, tag.typ)}</div>`;
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
    ...ereignisse.filter(x => x.ganztaegig && !karten.some(k => k.text === x.titel)).map(x => ({ zeit: -1, html: `<div class="eintrag"><div class="zeit">ganztägig</div>
        <div class="std veranstaltung"><span class="mks"><i class="mk-plus"></i></span><div class="z1">${e(x.titel)}</div>
        ${[gekuerzt(x.klassen.map(k => ({ k })), 4), gekuerzt(x.lehrer.map(k => ({ k })), 3), werte(x.raeume.map(k => ({ k })))].filter(Boolean).map(z => `<div class="z2">${z}</div>`).join('')}
        <div class="z3">Veranstaltung</div></div></div>` })),
    ...karten.map((k, i) => ({ zeit: minuten(k.beginn), html: `<div class="eintrag ${k.datum === jetztIso && minuten(k.beginn) <= jetzt && jetzt < minuten(k.ende) ? 'jetzt' : ''}">
        <div class="zeit"><b>${k.von === k.bis ? k.von + '.' : k.von + '.–' + k.bis + '.'}</b>${e(k.beginn)}–${e(k.ende)}</div>
        <div class="std ${e(kartenKlasse(k, typ, jetztIso, jetzt))}" data-karte="${i}" tabindex="0" role="button" title="${e(titelText(k))}" style="${fachStil(k)}">${karteInhalt(k, typ, true)}${k.status !== 'regulaer' && statusText(k) ? `<div class="z3">${e(statusText(k))}</div>` : ''}</div></div>` })),
    ...aufsichten.map(a => ({ zeit: minuten(a.beginn), html: `<div class="eintrag"><div class="zeit">${e(a.beginn)}–${e(a.ende)}</div>
        <div class="aufsicht ${e(a.status)}">Pausenaufsicht ${e(a.gangName)}${a.status === 'vertreten' ? ' – übernimmt ' + e(a.lehrer) : a.status === 'vertretung' ? ' – Vertretung für ' + e(a.alt) : ''}</div></div>` })),
  ].sort((a, b) => a.zeit - b.zeit);
  return `<div class="tt-liste">${eintraege.map(x => x.html).join('')}</div>`;
}
