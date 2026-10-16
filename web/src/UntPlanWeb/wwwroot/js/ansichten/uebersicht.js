// Übersicht: Karten-Dashboard im neuen Design (Titel orange, Untertitel grau, Pfeil rechts oben wie „Meine Daten“) – je nach Rolle
// Meine Stunden heute, Änderungen, Aufsichten, Klassenbuch-Hinweise, offene Abwesenheiten (Klassenleitung), Prüfungen,
// Hausaufgaben, Mitteilungen und Infos. Leere Karten zeigen ein großes graues Symbol.
import { e, holen, heuteIso, plusTage, montag, datumKurz, stundenText, WOCHENTAGE_KURZ, ausIso } from '../ui.js';
import { statusText, werte, kartenBinden, fachStil } from '../plangitter.js';

const OHNE_AENDERUNG = ['regulaer', 'bereitschaft'];
const wt = iso => WOCHENTAGE_KURZ[(ausIso(iso).getDay() + 6) % 7];
const tagText = (iso, heute) => iso === heute ? 'Heute' : iso === plusTage(heute, 1) ? 'Morgen' : `${wt(iso)} ${datumKurz(iso)}`;
const chipFarbe = k => ['entfall', 'abwesend'].includes(k.status) ? 'rot' : k.status === 'klausur' || k.status === 'offen' || k.vertretungOffen ? 'gelb' : k.status === 'veranstaltung' || k.status === 'buchung' ? 'grau' : 'lila';

/** Karte wie in „Meine Daten“: Symbol, Titel (orange), Untertitel (grau), Pfeil rechts oben; leer = großes graues Symbol. */
function karte({ symbol, titel, unter, href, inhalt, leer, leerSymbol, zahl, breit = false }) {
  const kopfZahl = zahl ? `<span class="ub-zahl">${zahl > 99 ? '99+' : zahl}</span>` : '';
  return `<section class="fm-karte fm-uebersicht ub-karte ${breit ? 'breit' : ''}">
    <header><i class="bi bi-${symbol} fm-sym" aria-hidden="true"></i><span class="titel">${e(titel)}</span>${kopfZahl}<span class="unter">${e(unter)}</span>
      ${href ? `<a href="${href}" class="fm-pfeil" title="${e(titel)} öffnen" aria-label="${e(titel)} öffnen"><i class="bi bi-arrow-right"></i></a>` : ''}</header>
    <div class="fm-inhalt">${inhalt || `<div class="fm-leer gross"><i class="bi bi-${leerSymbol || symbol}"></i><span>${e(leer)}</span></div>`}</div></section>`;
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

  const kartenHtml = [];
  if (element) {
    const frei = tag?.frei ? `<div class="fm-leer gross"><i class="bi bi-sun"></i><span>${e(tag.freiGrund || 'Heute ist unterrichtsfrei')}</span></div>` : '';
    kartenHtml.push(karte({
      symbol: 'calendar3', titel: kollegium ? 'Meine Stunden heute' : 'Stunden heute', unter: `${wt(heute)} ${datumKurz(heute)}`, href: '#/plan/mein',
      inhalt: frei || (heuteKarten.length ? `<ul class="fm-liste ub-liste" data-liste="heute">${heuteKarten.map((k, i) => stundeZeile(k, i, typ, false, heute)).join('')}</ul>` : ''),
      leer: 'Heute kein Unterricht', leerSymbol: 'calendar-x', zahl: 0,
    }));
    binden('heute', heuteKarten);
    kartenHtml.push(karte({
      symbol: 'arrow-left-right', titel: 'Vertretungen und Änderungen', unter: 'Diese und nächste Woche', href: kollegium ? '#/vertretungen' : '#/plan/mein',
      inhalt: aenderungen.length ? `<ul class="fm-liste ub-liste" data-liste="aenderungen">${aenderungen.slice(0, 8).map((k, i) => stundeZeile(k, i, typ, true, heute)).join('')}</ul>
        ${aenderungen.length > 8 ? `<a class="ub-mehr" href="#/plan/mein">+ ${aenderungen.length - 8} weitere</a>` : ''}` : '',
      leer: 'Keine Änderungen', leerSymbol: 'check2-circle', zahl: aenderungen.length,
    }));
    binden('aenderungen', aenderungen.slice(0, 8));
  }
  if (kollegium && element)
    kartenHtml.push(karte({
      symbol: 'person-walking', titel: 'Meine Aufsichten', unter: 'Nächste 7 Tage', href: '#/plan/mein',
      inhalt: aufsichten.length ? `<ul class="fm-liste">${aufsichten.slice(0, 8).map(a => `<li><span class="ub-tag">${e(tagText(a.datum, heute))}</span>
          <span>${e(a.gangName || a.gang)}</span><span class="ms-auto fz-grau text-nowrap">${e(a.beginn)}–${e(a.ende)}</span>${a.status === 'vertretung' ? '<span class="nl-chip lila">Vertretung</span>' : ''}</li>`).join('')}</ul>` : '',
      leer: 'Keine Aufsichten', leerSymbol: 'cup-hot',
    }));
  if (kollegium && element)
    kartenHtml.push(karte({
      symbol: 'journal-text', titel: 'Klassenbuch-Hinweise', unter: 'Letzte zwei Wochen', href: '#/offen',
      inhalt: offen.length ? `<p class="ub-hinweis"><b>${offen.length}</b> ${offen.length === 1 ? 'Stunde' : 'Stunden'} ohne Lehrstoff</p>
        <ul class="fm-liste">${offen.slice(-5).reverse().map(o => `<li><span class="ub-tag">${e(tagText(o.datum, heute))}</span><b>${e(o.fach)}</b><span>${e((o.klassen || []).join(', '))}</span>
          <span class="ms-auto fz-grau">${stundenText(o.von, o.bis)} Std.</span></li>`).join('')}</ul>` : '',
      leer: 'Alles eingetragen', leerSymbol: 'journal-check', zahl: offen.length,
    }));
  if (kollegium && ich.fehlzeiten)
    kartenHtml.push(karte({
      symbol: 'person-x', titel: 'Offene Abwesenheiten', unter: 'Klassenleitung · 30 Tage', href: '#/abwesenheiten',
      inhalt: abwesenheiten.length ? `<ul class="fm-liste">${abwesenheiten.slice(0, 6).map(z => `<li><b>${e(z.schueler)}</b><span class="fz-grau">${e(z.klasse)}</span>
          <span class="ms-auto fz-grau text-nowrap">${e(datumKurz(z.vonDatum))}${z.bisDatum && z.bisDatum !== z.vonDatum ? ` – ${e(datumKurz(z.bisDatum))}` : ''}</span><span class="nl-chip gelb">offen</span></li>`).join('')}</ul>` : '',
      leer: 'Keine offenen Abwesenheiten', leerSymbol: 'person-check', zahl: abwesenheiten.length,
    }));
  kartenHtml.push(karte({
    symbol: 'mortarboard', titel: 'Prüfungen', unter: 'Nächste 4 Wochen', href: '#/pruefungen',
    inhalt: pruefungen.length ? `<ul class="fm-liste">${pruefungen.slice(0, 6).map(p => `<li><span class="ub-tag">${e(tagText(p.datum, heute))}</span><b>${e(p.titel)}</b>
        <span class="fz-grau">${e(p.klassen.join(', '))}</span><span class="ms-auto fz-grau text-nowrap">${stundenText(p.stundeVon, p.stundeBis)} Std.${p.raeume.length ? ' · ' + e(p.raeume.join(', ')) : ''}</span>
        ${p.status === 'vorgemerkt' ? '<span class="nl-chip grau">vorgemerkt</span>' : ''}</li>`).join('')}</ul>` : '',
    leer: 'Keine Prüfungen', leerSymbol: 'mortarboard', zahl: pruefungen.length,
  }));
  kartenHtml.push(karte({
    symbol: 'house-door', titel: 'Hausaufgaben', unter: 'Fällig in den nächsten 4 Wochen', href: '#/hausaufgaben',
    inhalt: offeneHa.length ? `<ul class="fm-liste">${offeneHa.slice(0, 6).map(h => `<li><b>${e(h.fach)}</b><span class="fm-hwtext">${e(h.hausaufgabe)}</span>
        ${h.faellig ? `<span class="ms-auto fz-grau text-nowrap ${h.faellig <= plusTage(heute, 1) ? 'ub-bald' : ''}">bis ${e(tagText(h.faellig, heute))}</span>` : ''}</li>`).join('')}</ul>` : '',
    leer: 'Keine Hausaufgaben', leerSymbol: 'house-check',
  }));
  kartenHtml.push(karte({
    symbol: 'envelope', titel: 'Mitteilungen', unter: ungelesen.length ? `${ungelesen.length} ungelesen` : 'Posteingang', href: '#/mitteilungen',
    inhalt: mitteilungen.length ? `<ul class="fm-liste">${mitteilungen.slice(0, 5).map(m => `<li class="${m.gelesen ? '' : 'ub-neu'}">${m.gelesen ? '' : '<span class="ic-punkt" aria-label="ungelesen"></span>'}
        <b class="text-truncate">${e(m.absender)}</b><span class="text-truncate">${e(m.betreff || '(ohne Betreff)')}</span>${m.wichtig ? '<span class="nl-chip rot">wichtig</span>' : ''}</li>`).join('')}</ul>` : '',
    leer: 'Keine Mitteilungen', leerSymbol: 'envelope-open', zahl: ungelesen.length,
  }));
  kartenHtml.push(karte({
    symbol: 'megaphone', titel: 'Infos der Schule', unter: 'Infocenter', href: '#/neuigkeiten/infos',
    inhalt: infos.length ? `<ul class="fm-liste">${infos.slice(0, 4).map(x => `<li>${x.gelesen ? '' : '<span class="ic-punkt" aria-label="ungelesen"></span>'}<b class="text-truncate">${e(x.titel)}</b>
        ${x.wichtig ? '<span class="nl-chip rot">wichtig</span>' : ''}<span class="ms-auto fz-grau text-nowrap">${e(datumKurz(x.datum))}</span></li>`).join('')}</ul>` : '',
    leer: 'Keine Infos', leerSymbol: 'megaphone', zahl: infos.filter(x => !x.gelesen).length,
  }));

  seite.innerHTML = `<div class="fm-kopf"><h1 class="fm-titel">Übersicht</h1>${element ? `<span class="ub-element">${e(ich.elementName || element)}</span>` : ''}</div>
    <div class="fm-raster ub-raster">${kartenHtml.join('')}</div>`;
  for (const [name, liste] of listen) {
    const el = seite.querySelector(`[data-liste="${name}"]`);
    if (el && liste.length) kartenBinden(el, liste, kontext, typ);
  }
}
