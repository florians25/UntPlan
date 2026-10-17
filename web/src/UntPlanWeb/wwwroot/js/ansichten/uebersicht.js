// Übersicht für alle Rollen (Lehrkräfte, Schulleitung/Administration, Schüler*innen, Erziehungsberechtigte) im neuen Design.
// Ruhige Hierarchie, wichtigste Infos zuerst:
//   1. „Auf einen Blick“: wenige Kennzahlen-Kacheln (Zahl + Bezeichnung), jede führt zur passenden Seite.
//   2. „Als Nächstes“: Stunden des nächsten Unterrichtstags und Vertretungen/Änderungen (zwei breite Karten).
//   3. Abschnitte „Zu erledigen“ und „Termine und Mitteilungen“: nur Karten mit Inhalt (höchstens fünf Zeilen);
//      Leeres steht ruhig in einer Zeile darunter („Nichts offen: …“) statt als eigene Karte.
import { e, holen, heuteIso, plusTage, montag, datumKurz, datumLang, stundenText, WOCHENTAGE_KURZ, ausIso } from '../ui.js';
import { statusText, werte, kartenBinden, fachStil } from '../plangitter.js';
import { kinder, kindWahl, kindBinden, statusLabel } from './familie.js';

const OHNE_AENDERUNG = ['regulaer', 'bereitschaft'];
const wt = iso => WOCHENTAGE_KURZ[(ausIso(iso).getDay() + 6) % 7];
const tagText = (iso, heute) => iso === heute ? 'Heute' : iso === plusTage(heute, 1) ? 'Morgen' : `${wt(iso)} ${datumKurz(iso)}`;
const chipFarbe = k => ['entfall', 'abwesend'].includes(k.status) ? 'rot' : k.status === 'klausur' || k.status === 'offen' || k.vertretungOffen ? 'gelb' : k.status === 'veranstaltung' || k.status === 'buchung' ? 'grau' : 'lila';

const MAX = 5;

/** Kennzahl-Kachel: Symbol, Zahl (Medium), Bezeichnung und kurzer Zusatz; 0 erscheint zurückhaltend grau. */
function kachel({ symbol, zahl, titel, unter, href, ton = '' }) {
  const n = Number(zahl) || 0;
  return `<a class="ub-kachel ${n ? ton : 'null'}" href="${href}">
    <span class="ub-kachel-sym" aria-hidden="true"><i class="bi bi-${symbol}"></i></span>
    <span class="ub-kachel-zahl">${n > 99 ? '99+' : n}</span>
    <span class="ub-kachel-text"><span class="titel">${e(titel)}</span><span class="unter">${e(unter)}</span></span></a>`;
}

/** Einheitliche Karte: Symbol, Titel, Zähler, Untertitel, Pfeil; höchstens fünf Zeilen und „Alle anzeigen“. */
function karte({ symbol, titel, unter, href, zeilen = [], leer, leerSymbol, zahl, listeName = '', kurz }) {
  const kopfZahl = zahl ? `<span class="ub-zahl">${zahl > 99 ? '99+' : zahl}</span>` : '';
  const voll = zeilen.length > 0;
  const inhalt = voll
    ? `<ul class="fm-liste ub-liste"${listeName ? ` data-liste="${listeName}"` : ''}>${zeilen.slice(0, MAX).join('')}</ul>
       ${zeilen.length > MAX && href ? `<a class="ub-mehr" href="${href}">Alle anzeigen <span>(${zeilen.length})</span></a>` : ''}`
    : `<div class="ub-leer"><i class="bi bi-${leerSymbol || symbol}" aria-hidden="true"></i><span>${e(leer)}</span></div>`;
  return {
    voll, titel, kurz: kurz ?? titel, href,
    html: `<section class="fm-karte ub-karte ${voll ? 'voll' : 'leer'}">
    <header><i class="bi bi-${symbol} fm-sym" aria-hidden="true"></i><span class="titel">${e(titel)}</span>${kopfZahl}<span class="unter" title="${e(unter)}">${e(unter)}</span>
      ${href ? `<a href="${href}" class="fm-pfeil" title="${e(titel)} öffnen" aria-label="${e(titel)} öffnen"><i class="bi bi-arrow-right"></i></a>` : ''}</header>
    <div class="ub-inhalt">${inhalt}</div></section>`,
  };
}

/** Abschnitt mit Überschrift: gefüllte Karten im Raster, leere als eine ruhige Zeile mit Verweisen. */
function abschnitt(titel, karten, leerText = 'Nichts offen') {
  if (!karten.length) return '';
  const voll = karten.filter(k => k.voll), leer = karten.filter(k => !k.voll);
  const ruhig = leer.length
    ? `<p class="ub-ruhig"><i class="bi bi-check2-circle" aria-hidden="true"></i><span>${voll.length ? leerText : `${leerText} – alles erledigt`}:</span>
        ${leer.map(k => k.href ? `<a href="${k.href}">${e(k.kurz)}</a>` : `<span>${e(k.kurz)}</span>`).join('<span class="ub-punkt" aria-hidden="true">·</span>')}</p>` : '';
  return `<section class="ub-abschnitt" aria-label="${e(titel)}"><h2 class="ub-abschnitt-titel">${e(titel)}</h2>
    ${voll.length ? `<div class="ub-raster s${voll.length === 4 ? 2 : Math.min(3, voll.length)}">${voll.map(k => k.html).join('')}</div>` : ''}${ruhig}</section>`;
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

/** Nächster Unterrichtstag ab heute (heute, solange dort Stunden stehen) aus den geladenen Wochen. */
function naechsterTag(plaene, heute) {
  const tage = plaene.flatMap(p => p.tage ?? []).filter(t => t.datum >= heute).sort((a, b) => a.datum.localeCompare(b.datum));
  const karten = plaene.flatMap(p => p.karten ?? []);
  const tag = tage.find(t => !t.frei && karten.some(k => k.datum === t.datum)) ?? tage.find(t => t.datum === heute) ?? null;
  const liste = tag ? karten.filter(k => k.datum === tag.datum).sort((a, b) => a.von - b.von) : [];
  // ohne Stunden: erster freier Tag (z. B. Ferien) für den Hinweis
  const frei = liste.length ? null : tage.find(t => t.frei) ?? null;
  return { tag: liste.length ? tag : null, liste, frei, gesperrt: plaene.some(p => p.gesperrt) && !liste.length };
}

export async function zeigen(seite, kontext) {
  const ich = kontext.ich;
  kontext.titel('');
  const heute = heuteIso();
  const mo = montag(heute);
  const familie = !!kontext.familie;
  const kollegium = !!ich.kollegium;

  // Schüler*innen und Erziehungsberechtigte: gewähltes Kind (Klasse für Plan, Prüfungen, Hausaufgaben)
  let kindListe = [], kind = null;
  if (familie) {
    ({ liste: kindListe, kind } = await kinder());
    if (!kind) {
      seite.innerHTML = '<div class="fm-karte"><div class="fm-leer">Ihrem Konto ist noch keine Schülerin bzw. kein Schüler zugeordnet. Bitte wenden Sie sich an die Schule.</div></div>';
      return;
    }
  }
  const typ = familie ? 'klasse' : ich.typ;
  const element = familie ? kind.klasse : ich.element;
  const filter = !element ? '' : typ === 'klasse' ? `&klasse=${encodeURIComponent(element)}` : `&lehrer=${encodeURIComponent(element)}`;
  const leerePlan = { karten: [], tage: [], aufsichten: [] };
  const plan = d => element ? holen(`/api/app/plan?typ=${typ}&element=${encodeURIComponent(element)}&datum=${d}`).catch(() => leerePlan) : Promise.resolve(leerePlan);
  const [w1, w2, pruefungen, hausaufgaben, offen, mitteilungen, abwesenheiten, info, fam] = await Promise.all([
    plan(mo), plan(plusTage(mo, 7)),
    holen(`/api/app/pruefungen?von=${heute}&bis=${plusTage(heute, 28)}${filter}`).catch(() => []),
    familie ? [] : holen(`/api/app/hausaufgaben?von=${plusTage(heute, -7)}&bis=${plusTage(heute, 28)}${filter}`).catch(() => []),
    kollegium && element ? holen('/api/app/klassenbuch/offen').catch(() => []) : [],
    holen('/api/app/mitteilungen?ordner=eingang').catch(() => []),
    kollegium && ich.fehlzeiten ? holen(`/api/app/abwesenheiten/zeitraeume?von=${plusTage(heute, -30)}&bis=${heute}&status=offen`).catch(() => []) : [],
    holen('/api/app/infocenter').catch(() => ({ eintraege: [] })),
    familie ? holen(`/api/app/familie/uebersicht?schueler=${kind.id}`).catch(() => ({ offeneAbwesenheiten: [], hausaufgaben: [] })) : null,
  ]);
  const alleKarten = [...w1.karten, ...w2.karten];
  const naechster = naechsterTag([w1, w2], heute);
  const aenderungen = alleKarten.filter(k => k.datum >= heute && !OHNE_AENDERUNG.includes(k.status)).sort((a, b) => a.datum.localeCompare(b.datum) || a.von - b.von);
  const aufsichten = [...(w1.aufsichten ?? []), ...(w2.aufsichten ?? [])].filter(a => a.datum >= heute && a.datum <= plusTage(heute, 6) && a.status !== 'vertreten')
    .sort((a, b) => a.datum.localeCompare(b.datum) || a.nachStunde - b.nachStunde);
  const offeneHa = familie
    ? fam.hausaufgaben.map(h => ({ fach: h.fach, hausaufgabe: h.text, faellig: h.faellig }))
    : hausaufgaben.filter(h => (h.faellig ?? h.datum) >= heute).sort((a, b) => (a.faellig ?? a.datum).localeCompare(b.faellig ?? b.datum));
  const ungelesen = mitteilungen.filter(m => !m.gelesen);
  const infos = (info.eintraege ?? []).filter(x => x.art === 'info');
  const infosNeu = infos.filter(x => !x.gelesen);
  const offeneAbw = familie ? fam.offeneAbwesenheiten : abwesenheiten;
  const listen = [];
  const binden = (name, liste) => listen.push([name, liste]);

  // ---------------------------------------------------------------- 1. Auf einen Blick
  const stundenTitel = naechster.tag ? (naechster.tag.datum === heute ? 'Stunden heute' : `Stunden ${tagText(naechster.tag.datum, heute).replace('Morgen', 'morgen')}`) : 'Stunden';
  const kacheln = [];
  if (element) {
    kacheln.push(kachel({ symbol: 'calendar3', zahl: naechster.liste.filter(k => !['entfall', 'abwesend'].includes(k.status)).length, titel: stundenTitel,
      unter: kollegium ? 'Mein Stundenplan' : `Klasse ${element}`, href: '#/plan/mein' }));
    kacheln.push(kachel({ symbol: 'arrow-left-right', zahl: aenderungen.length, titel: 'Änderungen', unter: 'Zwei Wochen', href: kollegium ? '#/vertretungen' : '#/plan/mein', ton: 'lila' }));
  }
  if (kollegium && element) kacheln.push(kachel({ symbol: 'journal-text', zahl: offen.length, titel: 'Ohne Lehrstoff', unter: 'Letzte zwei Wochen', href: '#/offen', ton: 'akzent' }));
  if ((kollegium && ich.fehlzeiten) || familie) kacheln.push(kachel({ symbol: 'person-x', zahl: offeneAbw.length, titel: 'Abwesenheiten', unter: familie ? 'Offen · Schuljahr' : 'Offen · letzte 30 Tage', href: '#/abwesenheiten', ton: 'gelb' }));
  if (familie) kacheln.push(kachel({ symbol: 'house-door', zahl: offeneHa.length, titel: 'Hausaufgaben', unter: 'Nächste 7 Tage', href: '#/hausaufgaben' }));
  if (!element) {
    kacheln.push(kachel({ symbol: 'mortarboard', zahl: pruefungen.length, titel: 'Prüfungen', unter: 'Nächste 4 Wochen', href: '#/pruefungen' }));
    kacheln.push(kachel({ symbol: 'megaphone', zahl: infosNeu.length, titel: 'Neue Infos', unter: 'Infocenter', href: '#/neuigkeiten/infos' }));
  }
  kacheln.push(kachel({ symbol: 'envelope', zahl: ungelesen.length, titel: 'Ungelesen', unter: 'Mitteilungen', href: '#/mitteilungen', ton: 'akzent' }));

  // ---------------------------------------------------------------- 2. Als Nächstes
  const oben = [];
  if (element) {
    const t = naechster.tag, f = naechster.frei;
    const freiText = f ? `${f.freiGrund || 'Unterrichtsfrei'}${f.freiBis && f.freiBis > f.datum ? ` bis ${datumKurz(f.freiBis)}` : f.datum !== heute ? ` ab ${tagText(f.datum, heute)}` : ''}` : '';
    oben.push(karte({
      symbol: 'calendar3', titel: kollegium ? 'Mein Unterricht' : 'Unterricht', unter: t ? `${tagText(t.datum, heute)}${t.datum !== heute ? '' : ` · ${datumKurz(heute)}`}` : 'Diese und nächste Woche', href: '#/plan/mein',
      zeilen: naechster.liste.map((k, i) => stundeZeile(k, i, typ, false, heute)), listeName: 'naechster',
      leer: naechster.gesperrt ? 'Die Anzeige des Stundenplans ist beschränkt' : f ? freiText : 'Kein Unterricht in den nächsten Tagen',
      leerSymbol: naechster.gesperrt ? 'lock' : f ? 'sun' : 'calendar-x', zahl: naechster.liste.length,
    }));
    binden('naechster', naechster.liste.slice(0, MAX));
    oben.push(karte({
      symbol: 'arrow-left-right', titel: 'Vertretungen und Änderungen', unter: 'Diese und nächste Woche', href: kollegium ? '#/vertretungen' : '#/plan/mein',
      zeilen: aenderungen.map((k, i) => stundeZeile(k, i, typ, true, heute)), listeName: 'aenderungen',
      leer: 'Keine Änderungen', leerSymbol: 'check2-circle', zahl: aenderungen.length,
    }));
    binden('aenderungen', aenderungen.slice(0, MAX));
  }

  // ---------------------------------------------------------------- 3. Zu erledigen / Termine und Mitteilungen
  const erledigen = [];
  if (kollegium && element)
    erledigen.push(karte({
      symbol: 'journal-text', titel: 'Klassenbuch-Hinweise', kurz: 'Klassenbuch', unter: 'Ohne Lehrstoff · letzte zwei Wochen', href: '#/offen',
      zeilen: offen.slice().reverse().map(o => `<li><span class="ub-tag">${e(tagText(o.datum, heute))}</span><b>${e(o.fach)}</b><span class="text-truncate">${e((o.klassen || []).join(', '))}</span>
          <span class="ms-auto fz-grau text-nowrap">${stundenText(o.von, o.bis)} Std.</span></li>`),
      leer: 'Alles eingetragen', leerSymbol: 'journal-check', zahl: offen.length,
    }));
  if ((kollegium && ich.fehlzeiten) || familie)
    erledigen.push(karte({
      symbol: 'person-x', titel: 'Offene Abwesenheiten', kurz: 'Abwesenheiten', unter: familie ? 'Schuljahr' : 'Klassenleitung · 30 Tage', href: '#/abwesenheiten',
      zeilen: familie
        ? offeneAbw.map(z => `<li><span class="ub-tag">${e(tagText(z.vonDatum, heute))}</span><span class="text-truncate">${z.bisDatum && z.bisDatum !== z.vonDatum ? `bis ${e(tagText(z.bisDatum, heute))}` : e(z.vonZeit || '')}</span>
            <span class="ms-auto">${statusLabel(z.status)}</span></li>`)
        : offeneAbw.map(z => `<li><b class="text-truncate">${e(z.schueler)}</b><span class="fz-grau">${e(z.klasse)}</span>
            <span class="ms-auto fz-grau text-nowrap">${e(datumKurz(z.vonDatum))}${z.bisDatum && z.bisDatum !== z.vonDatum ? ` – ${e(datumKurz(z.bisDatum))}` : ''}</span><span class="nl-chip gelb">offen</span></li>`),
      leer: 'Keine offenen Abwesenheiten', leerSymbol: 'person-check', zahl: offeneAbw.length,
    }));
  if (kollegium && element)
    erledigen.push(karte({
      symbol: 'person-walking', titel: 'Meine Aufsichten', kurz: 'Aufsichten', unter: 'Nächste 7 Tage', href: '#/plan/mein',
      zeilen: aufsichten.map(a => `<li><span class="ub-tag">${e(tagText(a.datum, heute))}</span><b class="text-truncate">${e(a.gangName || a.gang)}</b>
          <span class="ms-auto fz-grau text-nowrap">${e(a.beginn)}–${e(a.ende)}</span>${a.status === 'vertretung' ? '<span class="nl-chip lila">Vertretung</span>' : ''}</li>`),
      leer: 'Keine Aufsichten', leerSymbol: 'cup-hot', zahl: aufsichten.length,
    }));
  const hausaufgabenKarte = karte({
    symbol: 'house-door', titel: 'Hausaufgaben', unter: familie ? 'Nächste 7 Tage' : 'Fällig in den nächsten 4 Wochen', href: '#/hausaufgaben',
    zeilen: offeneHa.map(h => `<li><b>${e(h.fach)}</b><span class="fm-hwtext text-truncate">${e(h.hausaufgabe)}</span>
        ${h.faellig ? `<span class="ms-auto fz-grau text-nowrap ${h.faellig <= plusTage(heute, 1) ? 'ub-bald' : ''}">bis ${e(tagText(h.faellig, heute))}</span>` : ''}</li>`),
    leer: 'Keine Hausaufgaben', leerSymbol: 'house-check', zahl: offeneHa.length,
  });
  if (familie) erledigen.push(hausaufgabenKarte);

  const termine = [];
  termine.push(karte({
    symbol: 'mortarboard', titel: 'Prüfungen', unter: 'Nächste 4 Wochen', href: '#/pruefungen',
    zeilen: pruefungen.map(p => `<li><span class="ub-tag">${e(tagText(p.datum, heute))}</span><b class="text-truncate">${e(p.titel)}</b>
        <span class="fz-grau text-truncate">${e(p.klassen.join(', '))}</span><span class="ms-auto fz-grau text-nowrap">${stundenText(p.stundeVon, p.stundeBis)} Std.${p.raeume.length ? ' · ' + e(p.raeume.join(', ')) : ''}</span>
        ${p.status === 'vorgemerkt' ? '<span class="nl-chip grau">vorgemerkt</span>' : ''}</li>`),
    leer: 'Keine Prüfungen', leerSymbol: 'mortarboard', zahl: pruefungen.length,
  }));
  if (!familie) termine.push(hausaufgabenKarte);
  termine.push(karte({
    symbol: 'envelope', titel: 'Mitteilungen', unter: ungelesen.length ? `${ungelesen.length} ungelesen · Posteingang` : 'Posteingang', href: '#/mitteilungen',
    zeilen: mitteilungen.map(m => `<li class="${m.gelesen ? '' : 'ub-neu'}">${m.gelesen ? '' : '<span class="ic-punkt" aria-label="ungelesen"></span>'}
        <b class="text-truncate">${e(m.absender)}</b><span class="text-truncate">${e(m.betreff || '(ohne Betreff)')}</span>${m.wichtig ? '<span class="nl-chip rot ms-auto">wichtig</span>' : ''}</li>`),
    leer: 'Keine Mitteilungen', leerSymbol: 'envelope-open', zahl: ungelesen.length,
  }));
  termine.push(karte({
    symbol: 'megaphone', titel: 'Infos der Schule', kurz: 'Infos der Schule', unter: 'Infocenter', href: '#/neuigkeiten/infos',
    zeilen: infos.map(x => `<li>${x.gelesen ? '' : '<span class="ic-punkt" aria-label="ungelesen"></span>'}<b class="text-truncate">${e(x.titel)}</b>
        ${x.wichtig ? '<span class="nl-chip rot">wichtig</span>' : ''}<span class="ms-auto fz-grau text-nowrap">${e(datumKurz(x.datum))}</span></li>`),
    leer: 'Keine Infos', leerSymbol: 'megaphone', zahl: infosNeu.length,
  }));

  const name = familie ? `${kind.vorname} ${kind.name} · ${kind.klasse}` : element ? (ich.elementName || element) : (ich.anzeigename || ich.login || '');
  seite.innerHTML = `<div class="ub-bereich">
    <div class="fm-kopf ub-kopf"><h1 class="fm-titel">Übersicht</h1><span class="ub-element">${e(name)}</span>
      <span class="ub-datum">${e(datumLang(heute))}</span>${familie ? kindWahl(kindListe, kind) : ''}</div>
    <section class="ub-abschnitt" aria-label="Auf einen Blick"><div class="ub-kacheln">${kacheln.join('')}</div></section>
    ${oben.length ? `<section class="ub-abschnitt" aria-label="Als Nächstes"><h2 class="ub-abschnitt-titel">Als Nächstes</h2>
      <div class="ub-raster ub-oben s${oben.length}">${oben.map(k => k.html).join('')}</div></section>` : ''}
    ${abschnitt('Zu erledigen', erledigen)}
    ${abschnitt('Termine und Mitteilungen', termine, 'Ohne neue Einträge')}
  </div>`;
  for (const [liste, karten] of listen) {
    const el = seite.querySelector(`[data-liste="${liste}"]`);
    if (el && karten.length) kartenBinden(el, karten, kontext, typ);
  }
  if (familie) kindBinden(seite, () => kontext.navigieren(location.hash));
}
