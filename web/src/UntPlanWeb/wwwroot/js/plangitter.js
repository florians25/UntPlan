// Darstellung der Stundenpläne: zeitbasiertes Wochenraster, Stundenkarten, Detaildialog mit Klassenbucheintrag.
import { e, icon, minuten, jetztMinuten, heuteIso, datumKurz, datumLang, dialog, holen, senden, toast, WOCHENTAGE_KURZ } from './ui.js';

const PIXEL_JE_MINUTE = 1.3;
const ERSATZFARBEN = ['#5b8def', '#e0733b', '#3aa676', '#b25fc7', '#d14d72', '#2e9fb3', '#c49a1d', '#7a7fd6', '#4b9e48', '#cf5c3c'];

export const STATUSTEXT = {
  geaendert: 'Änderung', offen: 'offen', entfall: 'Entfall', abwesend: 'vertreten', klausur: 'Klausur',
  bereitschaft: 'Bereitschaft', buchung: 'Buchung', vorgemerkt: 'vorgemerkt',
};

function farbe(k) {
  if (k.farbe && /^#[0-9a-f]{6}$/i.test(k.farbe)) return k.farbe;
  let h = 0;
  for (const z of k.fach || '?') h = (h * 31 + z.charCodeAt(0)) >>> 0;
  return ERSATZFARBEN[h % ERSATZFARBEN.length];
}

/** Werte (Lehrkraft, Raum …) mit durchgestrichenem Altwert bzw. „???“. */
export function werte(liste) {
  return (liste || []).map(w => {
    if (w.alt && !w.k) return `<span class="wert alt">${e(w.alt)}</span><span class="wert offen">???</span>`;
    if (w.alt) return `<span class="wert alt">${e(w.alt)}</span><span class="wert neu">${e(w.k)}</span>`;
    return `<span class="wert">${e(w.k)}</span>`;
  }).join(' ');
}

function statusText(k) {
  if (k.status === 'entfall' && k.art) return k.art.length > 14 ? 'Entfall' : k.art;
  if (k.status === 'geaendert' && k.art && k.art.length <= 14) return k.art;
  return STATUSTEXT[k.status] ?? '';
}

/** Inhalt einer Karte je nach Planart: Klassenplan zeigt Lehrkraft und Raum, Lehrerplan Klasse und Raum, Raumplan Klasse und Lehrkraft. */
export function karteInhalt(k, typ, kompakt = false) {
  const zeilen = typ === 'lehrer' ? [werte(k.klassen), werte(k.raeume)]
    : typ === 'raum' ? [werte(k.klassen), werte(k.lehrer)]
    : [werte(k.lehrer), werte(k.raeume)];
  if (typ === 'klasse' && k.klassen.length > 1) zeilen.push(`<span class="gedimmt">${e(k.klassen.map(x => x.k).join(', '))}</span>`);
  // ohne Fach (Buchung, Klausur, Veranstaltung): Titel bzw. Art als Überschrift
  const ueberschrift = k.fach || k.text || k.art || '—';
  const textZeile = k.text && ueberschrift !== k.text ? k.text : '';
  const fach = k.fachAlt ? `<span class="wert alt">${e(k.fachAlt)}</span><span>${e(k.fach)}</span>` : e(ueberschrift);
  const st = statusText(k);
  if (kompakt) {
    const z = zeilen.slice(0, 2).filter(x => x).join('<span class="trenner">·</span>');
    return `<div class="fach">${fach}</div>${z ? `<div class="zeile">${z}</div>` : ''}` + (st ? `<span class="status">${e(st)}</span>` : '');
  }
  return `<div class="fach">${fach}</div>${zeilen.filter(z => z).map(z => `<div class="zeile">${z}</div>`).join('')}` +
    (textZeile ? `<div class="text">${e(textZeile)}</div>` : '') + (st ? `<span class="status">${e(st)}</span>` : '');
}

function titelText(k) {
  const w = l => (l || []).map(x => x.k || '???').join(', ');
  return [k.fachName || k.fach, w(k.klassen), w(k.lehrer), w(k.raeume), statusText(k), k.text].filter(Boolean).join(' · ');
}

function vergangen(k, jetztIso, jetzt) {
  return k.datum < jetztIso || (k.datum === jetztIso && minuten(k.ende) <= jetzt);
}

/** Spuren für gleichzeitige Karten (nebeneinander statt übereinander). */
function spuren(karten) {
  const sortiert = [...karten].sort((a, b) => minuten(a.beginn) - minuten(b.beginn) || minuten(b.ende) - minuten(a.ende));
  const gruppen = [];
  let gruppe = null, gruppenEnde = -1;
  for (const k of sortiert) {
    const s = minuten(k.beginn), en = minuten(k.ende);
    if (!gruppe || s >= gruppenEnde) { gruppe = []; gruppen.push(gruppe); gruppenEnde = en; }
    gruppe.push(k);
    gruppenEnde = Math.max(gruppenEnde, en);
  }
  const res = new Map();
  for (const g of gruppen) {
    const enden = [];
    for (const k of g) {
      let spur = enden.findIndex(x => x <= minuten(k.beginn));
      if (spur < 0) { spur = enden.length; enden.push(0); }
      enden[spur] = minuten(k.ende);
      res.set(k, { spur, anzahl: 0 });
    }
    for (const k of g) res.get(k).anzahl = enden.length;
  }
  return res;
}

/**
 * Wochenraster als HTML. plan: Antwort von /api/app/plan. Optionen: typ, gewaehlterTag (Index für Mobilansicht).
 * Karten tragen data-karte (Index in plan.karten) für den Detaildialog.
 */
export function wochenGitter(plan, { gewaehlterTag = null } = {}) {
  const zeiten = plan.zeitraster.filter(z => z.beginn && z.ende);
  if (!zeiten.length) return '<div class="leer">Kein Zeitraster übertragen.</div>';
  const letzteBelegt = Math.max(6, ...plan.karten.map(k => k.bis), ...plan.aufsichten.map(a => a.nachStunde + 1));
  const sichtbar = zeiten.filter(z => z.nr <= letzteBelegt);
  const start = minuten(sichtbar[0].beginn);
  const ende = minuten(sichtbar[sichtbar.length - 1].ende);
  const hoehe = Math.round((ende - start) * PIXEL_JE_MINUTE) + 8;
  const y = hhmm => Math.round((minuten(hhmm) - start) * PIXEL_JE_MINUTE) + 4;
  const heute = heuteIso();
  const jetzt = jetztMinuten();
  const tage = plan.tage;
  const ausgewaehlt = gewaehlterTag ?? Math.max(0, tage.findIndex(t => t.datum === heute));

  let html = `<div class="plan ein-tag" style="--tage:${tage.length}">`;
  html += '<div class="plan-ecke"></div>';
  tage.forEach((t, i) => {
    html += `<div class="plan-tagkopf ${t.datum === heute ? 'heute' : ''} ${i === ausgewaehlt ? 'gewaehlt' : ''}">
      <b>${e(t.wochentag)}</b><small>${datumKurz(t.datum)}${t.woche ? ' · ' + e(t.woche) + '-Woche' : ''}</small>
      ${t.hinweise.map(h => `<span class="tag-hinweis">${e(h)}</span>`).join('')}</div>`;
  });
  // Zeitachse
  html += `<div class="plan-achse" style="height:${hoehe}px">` +
    sichtbar.map(z => `<div class="plan-zeit" style="top:${y(z.beginn)}px"><b>${e(z.bezeichnung)}</b>${e(z.beginn)}<br>${e(z.ende)}</div>`).join('') + '</div>';
  tage.forEach((t, i) => {
    html += `<div class="plan-spalte ${t.frei ? 'frei' : ''} ${i === ausgewaehlt ? 'gewaehlt' : ''}" style="height:${hoehe}px">`;
    html += sichtbar.map(z => `<div class="plan-raster-linie" style="top:${y(z.beginn)}px"></div>`).join('');
    if (t.frei) html += `<div class="plan-frei-text">${e(t.freiGrund || 'unterrichtsfrei')}</div>`;
    const karten = plan.karten.filter(k => k.datum === t.datum && k.beginn && k.ende);
    const lage = spuren(karten);
    for (const k of karten) {
      const { spur, anzahl } = lage.get(k);
      const breite = 100 / anzahl;
      const oben = y(k.beginn), unten = y(k.ende);
      const h = Math.max(unten - oben - 3, 26);
      const zeilenZahl = 2 + (k.text ? 1 : 0) + (plan.typ === 'klasse' && k.klassen.length > 1 ? 1 : 0);
      const kompakt = h < 26 + zeilenZahl * 17;
      html += `<div class="std ${e(k.status)} ${vergangen(k, heute, jetzt) ? 'vergangen' : ''} ${kompakt ? 'kompakt' : ''} ${anzahl > 1 ? 'schmal' : ''}" data-karte="${plan.karten.indexOf(k)}" tabindex="0" role="button"
        title="${e(titelText(k))}" style="--fach:${farbe(k)};top:${oben}px;height:${h}px;left:calc(${spur * breite}% + 3px);width:calc(${breite}% - 6px)">${karteInhalt(k, plan.typ, kompakt)}</div>`;
    }
    for (const a of plan.aufsichten.filter(a => a.datum === t.datum && a.beginn && a.ende)) {
      const oben = y(a.beginn), unten = y(a.ende);
      const h = Math.max(unten - oben, 16);
      const mitte = oben + (unten - oben) / 2 - h / 2;
      const text = a.status === 'vertreten' ? `Aufsicht ${a.gang} → ${a.lehrer}` : a.status === 'vertretung' ? `Aufsicht ${a.gang} (für ${a.alt})` : `Aufsicht ${a.gang}`;
      html += `<div class="aufsicht ${e(a.status)}" style="top:${mitte}px;height:${h}px" title="${e(a.gangName)} ${e(a.beginn)}–${e(a.ende)}">${e(text)}</div>`;
    }
    if (t.datum === heute && jetzt >= start && jetzt <= ende) html += `<div class="plan-jetzt" style="top:${y(String(Math.floor(jetzt / 60)).padStart(2, '0') + ':' + String(jetzt % 60).padStart(2, '0'))}px"></div>`;
    html += '</div>';
  });
  html += '</div>';
  return html;
}

export function tagAuswahl(plan, gewaehlt) {
  return `<div class="tagauswahl">${plan.tage.map((t, i) => `<button data-tagwahl="${i}" class="${i === gewaehlt ? 'aktiv' : ''}"><b>${WOCHENTAGE_KURZ[i]}</b><small>${datumKurz(t.datum)}</small></button>`).join('')}</div>`;
}

export function legende(mitAufsicht) {
  return `<div class="legende">
    <span><i class="l-geaendert"></i>Änderung / Vertretung</span><span><i class="l-offen"></i>offen</span>
    <span><i class="l-entfall"></i>Entfall</span><span><i class="l-klausur"></i>Klausur</span>
    <span><i class="l-bereitschaft"></i>Bereitschaft</span><span><i class="l-vorgemerkt"></i>vorgemerkt</span>
    ${mitAufsicht ? '<span><i class="l-aufsicht"></i>Pausenaufsicht</span>' : ''}</div>`;
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
  return `<tr><th>${titel}</th><td>${werte(liste)}</td></tr>`;
}

/** Detaildialog einer Stunde; Kollegium kann (bei eigener Stunde) den Klassenbucheintrag bearbeiten. */
export async function karteDialog(k, kontext) {
  const zeit = `${datumLang(k.datum)}, ${k.von === k.bis ? k.von + '. Stunde' : k.von + '.–' + k.bis + '. Stunde'}${k.beginn ? ` (${k.beginn}–${k.ende})` : ''}`;
  const st = statusText(k);
  let inhalt = `<p class="gedimmt">${e(zeit)}</p>
    ${st ? `<p><span class="marker ${k.status === 'entfall' || k.status === 'offen' ? 'rot' : k.status === 'klausur' ? 'gelb' : k.status === 'bereitschaft' ? 'violett' : 'gruen'}">${e(st)}</span></p>` : ''}
    <table class="tabelle"><tbody>
      <tr><th>Fach</th><td>${k.fachAlt ? `<span class="alt">${e(k.fachAlt)}</span>` : ''}${e(k.fachName || k.fach)}${k.fachName && k.fachName !== k.fach ? ` (${e(k.fach)})` : ''}</td></tr>
      ${zeileWert('Klasse(n)', k.klassen)}${zeileWert('Lehrkraft', k.lehrer)}${zeileWert('Raum', k.raeume)}
      ${k.art ? `<tr><th>Art</th><td>${e(k.art)}</td></tr>` : ''}
      ${k.text ? `<tr><th>Hinweis</th><td>${e(k.text)}</td></tr>` : ''}
    </tbody></table>
    <div data-klassenbuch></div>`;
  const box = dialog({ titel: `${k.fach || k.art || 'Stunde'}${k.klassen.length ? ' · ' + k.klassen.map(x => x.k).join(', ') : ''}`, inhalt });
  const ziel = box.querySelector('[data-klassenbuch]');
  if (!k.unterricht || ['entfall', 'vorgemerkt', 'buchung'].includes(k.status)) return;
  try {
    const r = await holen(`/api/app/klassenbuch/stunde?datum=${k.datum}&stunde=${k.von}&unterricht=${k.unterricht}`);
    const eintrag = r.eintrag;
    if (r.darf) {
      ziel.innerHTML = `<h3 style="margin-top:18px">${icon('klassenbuch', 'icon-klein')} Klassenbuch</h3>
        <div class="formular">
          <label class="feld">Unterrichtsinhalt<textarea name="thema" rows="2" placeholder="Thema der Stunde">${e(eintrag?.thema)}</textarea></label>
          <div class="zeile">
            <label class="feld">Hausaufgabe<textarea name="hausaufgabe" rows="2">${e(eintrag?.hausaufgabe)}</textarea></label>
            <label class="feld">fällig am<input type="date" name="faellig" value="${e(eintrag?.faellig ?? '')}"></label>
          </div>
          <label class="feld">Notiz (nur Kollegium)<input name="notiz" value="${e(eintrag?.notiz)}"></label>
          <div><button class="knopf haupt" data-speichern>${icon('haken', 'icon-klein')} Speichern</button>
          ${eintrag ? `<span class="gedimmt klein" style="margin-left:8px">zuletzt geändert ${e(eintrag.geaendert)}</span>` : ''}</div>
        </div>`;
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
      ziel.innerHTML = `<h3 style="margin-top:18px">Klassenbuch</h3><table class="tabelle"><tbody>
        ${eintrag.thema ? `<tr><th>Inhalt</th><td>${e(eintrag.thema)}</td></tr>` : ''}
        ${eintrag.hausaufgabe ? `<tr><th>Hausaufgabe</th><td>${e(eintrag.hausaufgabe)}${eintrag.faellig ? ` <span class="marker petrol">bis ${datumKurz(eintrag.faellig)}</span>` : ''}</td></tr>` : ''}
        </tbody></table>`;
    }
  } catch { /* Klassenbuch optional */ }
}

/** Tagesübersicht (Elemente × Stunden). */
export function tagesGitter(tag) {
  const zeiten = tag.zeitraster;
  const max = Math.max(6, ...tag.zeilen.flatMap(z => z.karten.map(k => k.bis)));
  const spalten = zeiten.filter(z => z.nr <= max);
  let html = `<div class="tagplan"><div class="tagplan-gitter" style="grid-template-columns:150px repeat(${spalten.length}, minmax(96px, 1fr))">`;
  html += '<div class="tagplan-kopf"></div>' + spalten.map(z => `<div class="tagplan-kopf"><b>${e(z.bezeichnung)}</b>${e(z.beginn)}–${e(z.ende)}</div>`).join('');
  const alle = [];
  for (const zeile of tag.zeilen) {
    html += `<div class="tagplan-element">${e(zeile.kurz)}<small>${e(zeile.name !== zeile.kurz ? zeile.name : '')}</small></div>`;
    // je Zelle gleichzeitige Karten untereinander; Blöcke über mehrere Spalten
    const belegt = new Set();
    let spalte = 1;
    const sortiert = [...zeile.karten].sort((a, b) => a.von - b.von);
    for (const z of spalten) {
      if (belegt.has(z.nr)) continue;
      const hier = sortiert.filter(k => k.von === z.nr);
      if (!hier.length) { html += `<div class="tagplan-zelle"></div>`; continue; }
      const bis = Math.min(max, Math.max(...hier.map(k => k.bis)));
      for (let n = z.nr + 1; n <= bis; n++) belegt.add(n);
      html += `<div class="tagplan-zelle" style="grid-column:span ${bis - z.nr + 1}">` + hier.map(k => {
        alle.push(k);
        return `<div class="std ${e(k.status)}" data-karte="${alle.length - 1}" tabindex="0" role="button" style="--fach:${farbe(k)}">${karteInhalt(k, tag.typ)}</div>`;
      }).join('') + '</div>';
      spalte++;
    }
  }
  html += '</div></div>';
  return { html, karten: alle };
}

/** Kompakte Liste eines Tages (Startseite). */
export function tagesListe(karten, aufsichten, typ) {
  const jetztIso = heuteIso(), jetzt = jetztMinuten();
  const eintraege = [
    ...karten.map((k, i) => ({ zeit: minuten(k.beginn), html: `<div class="std ${e(k.status)} ${vergangen(k, jetztIso, jetzt) ? 'vergangen' : ''}" data-karte="${i}" tabindex="0" role="button" style="--fach:${farbe(k)}">
        <div class="zeitblock"><b>${k.von === k.bis ? k.von + '.' : k.von + '.–' + k.bis + '.'}</b>${e(k.beginn)}–${e(k.ende)}</div>
        <div class="mitte">${karteInhalt(k, typ).replace('<span class="status">', '<span class="status" hidden>')}</div>
        ${statusText(k) ? `<span class="status marker">${e(statusText(k))}</span>` : ''}</div>` })),
    ...aufsichten.map(a => ({ zeit: minuten(a.beginn), html: `<div class="aufsicht ${e(a.status)}">${icon('uhr', 'icon-klein')} ${e(a.beginn)}–${e(a.ende)} Pausenaufsicht ${e(a.gangName)}${a.status === 'vertreten' ? ' – übernimmt ' + e(a.lehrer) : a.status === 'vertretung' ? ' – Vertretung für ' + e(a.alt) : ''}</div>` })),
  ].sort((a, b) => a.zeit - b.zeit);
  return `<div class="tagesliste">${eintraege.map(x => x.html).join('')}</div>`;
}
