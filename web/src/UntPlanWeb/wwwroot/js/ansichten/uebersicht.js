// Übersicht für Lehrkräfte und Administration: ruhiges, koordiniertes Karten-Raster im neuen Design.
// Oben zwei breite Karten (Meine Stunden heute, Vertretungen und Änderungen), darunter gleich breite Karten mit einheitlicher
// Kopfzeile und höchstens fünf Listenzeilen; leere Karten erscheinen kompakt (kleines Symbol, eine Zeile) am Ende.
import { e, holen, heuteIso, plusTage, montag, datumKurz, stundenText, WOCHENTAGE_KURZ, ausIso } from '../ui.js';
import { statusText, werte, kartenBinden, fachStil } from '../plangitter.js';

const OHNE_AENDERUNG = ['regulaer', 'bereitschaft'];
const wt = iso => WOCHENTAGE_KURZ[(ausIso(iso).getDay() + 6) % 7];
const tagText = (iso, heute) => iso === heute ? 'Heute' : iso === plusTage(heute, 1) ? 'Morgen' : `${wt(iso)} ${datumKurz(iso)}`;
const chipFarbe = k => ['entfall', 'abwesend'].includes(k.status) ? 'rot' : k.status === 'klausur' || k.status === 'offen' || k.vertretungOffen ? 'gelb' : k.status === 'veranstaltung' || k.status === 'buchung' ? 'grau' : 'lila';

const MAX = 5;

/** Einheitliche Karte: Symbol, Titel (orange), Zähler, Untertitel (grau, einzeilig), Pfeil rechts.
 *  Listen zeigen höchstens fünf Zeilen und darunter „Alle anzeigen“; leere Karten sind kompakt (kleines Symbol, eine Zeile). */
function karte({ symbol, titel, unter, href, zeilen = [], leer, leerSymbol, zahl, breit = false, listeName = '', vorspann = '' }) {
  const kopfZahl = zahl ? `<span class="ub-zahl">${zahl > 99 ? '99+' : zahl}</span>` : '';
  const voll = zeilen.length > 0 || !!vorspann;
  const inhalt = vorspann || (zeilen.length
    ? `<ul class="fm-liste ub-liste"${listeName ? ` data-liste="${listeName}"` : ''}>${zeilen.slice(0, MAX).join('')}</ul>
       ${zeilen.length > MAX && href ? `<a class="ub-mehr" href="${href}">Alle anzeigen <span>(${zeilen.length})</span></a>` : ''}`
    : `<div class="ub-leer"><i class="bi bi-${leerSymbol || symbol}" aria-hidden="true"></i><span>${e(leer)}</span></div>`);
  return { voll, breit, html: `<section class="fm-karte ub-karte ${breit ? 'breit' : ''} ${voll ? 'voll' : 'leer'}">
    <header><i class="bi bi-${symbol} fm-sym" aria-hidden="true"></i><span class="titel">${e(titel)}</span>${kopfZahl}<span class="unter" title="${e(unter)}">${e(unter)}</span>
      ${href ? `<a href="${href}" class="fm-pfeil" title="${e(titel)} öffnen" aria-label="${e(titel)} öffnen"><i class="bi bi-arrow-right"></i></a>` : ''}</header>
    <div class="ub-inhalt">${inhalt}</div></section>` };
}

/** Zeile einer Stunde (Plan-Karte) mit Fachbalken, Zeit, Fach/Klassen/Raum und Status-Label. */
function stundeZeile(k, i, typ, mitTag, heute) {
  const entfall = ['entfall', 'abwesend'].includes(k.status);
  const was = [e(k.fach || k.text || k.art || '–'), typ !== 'klasse' && k.klassen.length ? werte(k.klassen, entfall) : '', werte(k.raeume, entfall)].filter(Boolean).join(' · ');
  const status = !OHNE_AENDERUNG.includes(k.status) ? `<span class="nl-chip ${chipFarbe(k)}">${e(statusText(k))}</span>` : '';
  return `<li class="ub-stunde ${entfall ? 'entfall' : ''}" role="button" tabindex="0" data-karte="${i}" style="${fachStil(k)}">
    <span class="ub-balken"></span>
    <span class="ub-zeit"><b>${mitTag ? e(tagText(k.datum, heute)) : e(k.beginn || stundenText(k.von, k.bis))}</b><small>${mitTag ? `${stundenText(k.von, k.bis)} Std.` : e(k.ende || '')}</small></span>
    <span class="ub-was">${was}${k.text && k.fach && !OHNE_AENDERUNG.includes(k.status) ? `<small>${e(k.text)}</small>` : ''}</span>${status}</li>`;
}

export async function zeigen(seite, kontext) {
  // Schüler*innen und Erziehungsberechtigte: „Meine Daten“
  if (kontext.familie) return (await import('./familie.js')).uebersicht(seite, kontext);
  const ich = kontext.ich;
  kontext.titel('');
  const heute = heuteIso();
  const mo = montag(heute);
  const typ = ich.typ, element = ich.element;
  const kollegium = !!ich.kollegium;
  const filter = !element ? '' : typ === 'klasse' ? `&klasse=${encodeURIComponent(element)}` : `&lehrer=${encodeURIComponent(element)}`;
  const leerePlan = { karten: [], tage: [], aufsichten: [] };
  const plan = d => element ? holen(`/api/app/plan?typ=${typ}&element=${encodeURIComponent(element)}&datum=${d}`).catch(() => leerePlan) : Promise.resolve(leerePlan);
  const [w1, w2, pruefungen, hausaufgaben, offen, mitteilungen, abwesenheiten, info] = await Promise.all([
    plan(mo), plan(plusTage(mo, 7)),
    holen(`/api/app/pruefungen?von=${heute}&bis=${plusTage(heute, 28)}${filter}`).catch(() => []),
    holen(`/api/app/hausaufgaben?von=${plusTage(heute, -7)}&bis=${plusTage(heute, 28)}${filter}`).catch(() => []),
    kollegium && element ? holen('/api/app/klassenbuch/offen').catch(() => []) : [],
    holen('/api/app/mitteilungen?ordner=eingang').catch(() => []),
    kollegium && ich.fehlzeiten ? holen(`/api/app/abwesenheiten/zeitraeume?von=${plusTage(heute, -30)}&bis=${heute}&status=offen`).catch(() => []) : [],
    holen('/api/app/infocenter').catch(() => ({ eintraege: [] })),
  ]);
  const alleKarten = [...w1.karten, ...w2.karten];
  const heuteKarten = alleKarten.filter(k => k.datum === heute).sort((a, b) => a.von - b.von);
  const aenderungen = alleKarten.filter(k => k.datum >= heute && !OHNE_AENDERUNG.includes(k.status)).sort((a, b) => a.datum.localeCompare(b.datum) || a.von - b.von);
  const aufsichten = [...(w1.aufsichten ?? []), ...(w2.aufsichten ?? [])].filter(a => a.datum >= heute && a.datum <= plusTage(heute, 6) && a.status !== 'vertreten')
    .sort((a, b) => a.datum.localeCompare(b.datum) || a.nachStunde - b.nachStunde);
  const offeneHa = hausaufgaben.filter(h => (h.faellig ?? h.datum) >= heute).sort((a, b) => (a.faellig ?? a.datum).localeCompare(b.faellig ?? b.datum));
  const ungelesen = mitteilungen.filter(m => !m.gelesen);
  const infos = (info.eintraege ?? []).filter(x => x.art === 'info');
  const tag = (w1.tage ?? []).find(t => t.datum === heute);
  const listen = [];
  const binden = (k, liste) => { listen.push([k, liste]); return k; };

  const oben = [], karten = [];
  if (element) {
    oben.push(karte({
      symbol: 'calendar3', titel: kollegium ? 'Meine Stunden heute' : 'Stunden heute', unter: `${wt(heute)} ${datumKurz(heute)}`, href: '#/plan/mein', breit: true,
      zeilen: tag?.frei ? [] : heuteKarten.map((k, i) => stundeZeile(k, i, typ, false, heute)), listeName: 'heute',
      leer: tag?.frei ? (tag.freiGrund || 'Heute ist unterrichtsfrei') : 'Heute kein Unterricht', leerSymbol: tag?.frei ? 'sun' : 'calendar-x', zahl: tag?.frei ? 0 : heuteKarten.length,
    }));
    binden('heute', tag?.frei ? [] : heuteKarten.slice(0, MAX));
    oben.push(karte({
      symbol: 'arrow-left-right', titel: 'Vertretungen und Änderungen', unter: 'Diese und nächste Woche', href: kollegium ? '#/vertretungen' : '#/plan/mein', breit: true,
      zeilen: aenderungen.map((k, i) => stundeZeile(k, i, typ, true, heute)), listeName: 'aenderungen',
      leer: 'Keine Änderungen', leerSymbol: 'check2-circle', zahl: aenderungen.length,
    }));
    binden('aenderungen', aenderungen.slice(0, MAX));
  }
  if (kollegium && element)
    karten.push(karte({
      symbol: 'person-walking', titel: 'Meine Aufsichten', unter: 'Nächste 7 Tage', href: '#/plan/mein',
      zeilen: aufsichten.map(a => `<li><span class="ub-tag">${e(tagText(a.datum, heute))}</span><b class="text-truncate">${e(a.gangName || a.gang)}</b>
          <span class="ms-auto fz-grau text-nowrap">${e(a.beginn)}–${e(a.ende)}</span>${a.status === 'vertretung' ? '<span class="nl-chip lila">Vertretung</span>' : ''}</li>`),
      leer: 'Keine Aufsichten', leerSymbol: 'cup-hot', zahl: aufsichten.length,
    }));
  if (kollegium && element)
    karten.push(karte({
      symbol: 'journal-text', titel: 'Klassenbuch-Hinweise', unter: 'Ohne Lehrstoff · letzte zwei Wochen', href: '#/offen',
      zeilen: offen.slice().reverse().map(o => `<li><span class="ub-tag">${e(tagText(o.datum, heute))}</span><b>${e(o.fach)}</b><span class="text-truncate">${e((o.klassen || []).join(', '))}</span>
          <span class="ms-auto fz-grau text-nowrap">${stundenText(o.von, o.bis)} Std.</span></li>`),
      leer: 'Alles eingetragen', leerSymbol: 'journal-check', zahl: offen.length,
    }));
  if (kollegium && ich.fehlzeiten)
    karten.push(karte({
      symbol: 'person-x', titel: 'Offene Abwesenheiten', unter: 'Klassenleitung · 30 Tage', href: '#/abwesenheiten',
      zeilen: abwesenheiten.map(z => `<li><b class="text-truncate">${e(z.schueler)}</b><span class="fz-grau">${e(z.klasse)}</span>
          <span class="ms-auto fz-grau text-nowrap">${e(datumKurz(z.vonDatum))}${z.bisDatum && z.bisDatum !== z.vonDatum ? ` – ${e(datumKurz(z.bisDatum))}` : ''}</span><span class="nl-chip gelb">offen</span></li>`),
      leer: 'Keine offenen Abwesenheiten', leerSymbol: 'person-check', zahl: abwesenheiten.length,
    }));
  karten.push(karte({
    symbol: 'mortarboard', titel: 'Prüfungen', unter: 'Nächste 4 Wochen', href: '#/pruefungen',
    zeilen: pruefungen.map(p => `<li><span class="ub-tag">${e(tagText(p.datum, heute))}</span><b class="text-truncate">${e(p.titel)}</b>
        <span class="fz-grau text-truncate">${e(p.klassen.join(', '))}</span><span class="ms-auto fz-grau text-nowrap">${stundenText(p.stundeVon, p.stundeBis)} Std.${p.raeume.length ? ' · ' + e(p.raeume.join(', ')) : ''}</span>
        ${p.status === 'vorgemerkt' ? '<span class="nl-chip grau">vorgemerkt</span>' : ''}</li>`),
    leer: 'Keine Prüfungen', leerSymbol: 'mortarboard', zahl: pruefungen.length,
  }));
  karten.push(karte({
    symbol: 'house-door', titel: 'Hausaufgaben', unter: 'Fällig in den nächsten 4 Wochen', href: '#/hausaufgaben',
    zeilen: offeneHa.map(h => `<li><b>${e(h.fach)}</b><span class="fm-hwtext text-truncate">${e(h.hausaufgabe)}</span>
        ${h.faellig ? `<span class="ms-auto fz-grau text-nowrap ${h.faellig <= plusTage(heute, 1) ? 'ub-bald' : ''}">bis ${e(tagText(h.faellig, heute))}</span>` : ''}</li>`),
    leer: 'Keine Hausaufgaben', leerSymbol: 'house-check', zahl: offeneHa.length,
  }));
  karten.push(karte({
    symbol: 'envelope', titel: 'Mitteilungen', unter: ungelesen.length ? `${ungelesen.length} ungelesen · Posteingang` : 'Posteingang', href: '#/mitteilungen',
    zeilen: mitteilungen.map(m => `<li class="${m.gelesen ? '' : 'ub-neu'}">${m.gelesen ? '' : '<span class="ic-punkt" aria-label="ungelesen"></span>'}
        <b class="text-truncate">${e(m.absender)}</b><span class="text-truncate">${e(m.betreff || '(ohne Betreff)')}</span>${m.wichtig ? '<span class="nl-chip rot ms-auto">wichtig</span>' : ''}</li>`),
    leer: 'Keine Mitteilungen', leerSymbol: 'envelope-open', zahl: ungelesen.length,
  }));
  karten.push(karte({
    symbol: 'megaphone', titel: 'Infos der Schule', unter: 'Infocenter', href: '#/neuigkeiten/infos',
    zeilen: infos.map(x => `<li>${x.gelesen ? '' : '<span class="ic-punkt" aria-label="ungelesen"></span>'}<b class="text-truncate">${e(x.titel)}</b>
        ${x.wichtig ? '<span class="nl-chip rot">wichtig</span>' : ''}<span class="ms-auto fz-grau text-nowrap">${e(datumKurz(x.datum))}</span></li>`),
    leer: 'Keine Infos', leerSymbol: 'megaphone', zahl: infos.filter(x => !x.gelesen).length,
  }));

  // Reihenfolge: oben die beiden breiten Karten, darunter gefüllte Karten im gleichmäßigen Raster, leere Karten kompakt am Ende
  // 6er-Raster mit drei Karten je Reihe. Bleibt die letzte Reihe gefüllter Karten unvollständig, füllen gestapelte kompakte
  // (leere) Karten die freien Plätze; übrige Reste werden als zwei halbe bzw. eine ganze Karte gesetzt, sodass jede Reihe voll ist.
  const breiten = n => Array.from({ length: n }, (_, i) => n === 1 ? 6 : (n % 3 === 2 && i >= n - 2) || (n % 3 === 1 && i >= n - 4) ? 3 : 2);
  const mitBreite = (html, sp) => html.replace('<section class="', `<section style="--sp:${sp}" class="`);
  const voll = karten.filter(k => k.voll), leer = karten.filter(k => !k.voll);
  const frei = voll.length % 3 ? 3 - voll.length % 3 : 0;
  const zelle = [], rest = [...leer];
  if (frei && leer.length) {
    voll.forEach(k => zelle.push(mitBreite(k.html, 2)));
    const proStapel = Math.min(3, Math.ceil(leer.length / frei));
    for (let j = 0; j < frei && rest.length; j++)
      zelle.push(`<div class="ub-stapel" style="--sp:2">${rest.splice(0, proStapel).map(k => k.html).join('')}</div>`);
  } else breiten(voll.length).forEach((sp, i) => zelle.push(mitBreite(voll[i].html, sp)));
  const raster = (inhalt, klasse = '') => inhalt.length ? `<div class="ub-raster ${klasse}">${inhalt.join('')}</div>` : '';
  seite.innerHTML = `<div class="ub-bereich"><div class="fm-kopf"><h1 class="fm-titel">Übersicht</h1>${element ? `<span class="ub-element">${e(ich.elementName || element)}</span>` : ''}</div>
    ${raster(oben.map(k => mitBreite(k.html, oben.length === 1 ? 6 : 3)), 'ub-oben')}${raster(zelle)}
    ${raster(breiten(rest.length).map((sp, i) => mitBreite(rest[i].html, sp)), 'ub-kompakt')}</div>`;
  for (const [name, liste] of listen) {
    const el = seite.querySelector(`[data-liste="${name}"]`);
    if (el && liste.length) kartenBinden(el, liste, kontext, typ);
  }
}
