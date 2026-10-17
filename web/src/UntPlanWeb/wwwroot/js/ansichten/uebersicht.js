// Übersicht für alle Rollen (Lehrkräfte, Schulleitung/Administration, Schüler*innen, Erziehungsberechtigte) im neuen Design.
// Ruhiger Aufbau mit klarer Hierarchie:
//   1. Kopf: Begrüßung und eine Tageszeile (Datum, nächste Stunde bzw. „Heute kein Unterricht“, Ferienhinweis).
//   2. Kennzahlen: eine schmale Leiste mit wenigen anklickbaren Zahlen; nur Werte > 0 sind hervorgehoben.
//   3. Raster mit zwei gleich breiten Spalten und gleich hohen Zeilen (Karten einer Zeile enden bündig, keine Lücken):
//      „Heute & nächste Tage“ (Stunden mit Vertretungen/Änderungen zusammengeführt) | „Zu erledigen“,
//      darunter „Termine“ | „Mitteilungen & Infos“. Bei ungerader Anzahl geht die letzte Karte über beide Spalten.
// Jede Karte besteht aus Gruppen (höchstens fünf Zeilen, sonst „Alle anzeigen“); leere Gruppen erscheinen nicht einzeln,
// sondern zusammengefasst in einer grauen Zeile. Kartenköpfe neutral (dunkelgrauer Titel), Akzent nur als Symbol und Link.
// Die Stile stehen in css/uebersicht.css (wird beim ersten Aufruf geladen). Mobil einspaltig.
import { e, holen, heuteIso, plusTage, montag, datumKurz, stundenText, WOCHENTAGE, WOCHENTAGE_KURZ, ausIso, minuten, jetztMinuten } from '../ui.js';
import { statusText, kartenBinden, fachStil } from '../plangitter.js';
import { kinder, kindWahl, kindBinden } from './familie.js';

export const MAX = 5;
const OHNE_AENDERUNG = ['regulaer', 'bereitschaft'];
const ENTFALL = ['entfall', 'abwesend'];

function stileLaden() {
  if (document.querySelector('link[data-uebersicht-css]')) return;
  const l = document.createElement('link');
  l.rel = 'stylesheet';
  l.href = new URL('../../css/uebersicht.css', import.meta.url).href;
  l.dataset.uebersichtCss = '1';
  document.head.append(l);
}

const wtKurz = iso => WOCHENTAGE_KURZ[(ausIso(iso).getDay() + 6) % 7];
const wtLang = iso => WOCHENTAGE[(ausIso(iso).getDay() + 6) % 7];
const tagKurz = iso => `${wtKurz(iso)} ${datumKurz(iso)}`;
/** „Heute“, „Morgen“ oder „Fr 16.10.“ */
const tagRel = (iso, heute) => iso === heute ? 'Heute' : iso === plusTage(heute, 1) ? 'Morgen' : tagKurz(iso);
/** Gruppentitel eines Tages: „Heute · Mittwoch 14.10.“ */
const tagTitel = (iso, heute) => `${iso === heute ? 'Heute · ' : iso === plusTage(heute, 1) ? 'Morgen · ' : ''}${wtLang(iso)} ${datumKurz(iso)}`;
const MONATE = ['Januar', 'Februar', 'März', 'April', 'Mai', 'Juni', 'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember'];
const datumVoll = iso => { const d = ausIso(iso); return `${wtLang(iso)}, ${d.getDate()}. ${MONATE[d.getMonth()]} ${d.getFullYear()}`; };
const chipFarbe = k => ENTFALL.includes(k.status) ? 'rot' : k.status === 'klausur' || k.status === 'offen' || k.vertretungOffen ? 'gelb' : k.status === 'veranstaltung' || k.status === 'buchung' ? 'grau' : 'lila';
const anzahl = n => n > 99 ? '99+' : String(n);
const gruss = () => { const h = new Date().getHours(); return h < 11 ? 'Guten Morgen' : h < 18 ? 'Guten Tag' : 'Guten Abend'; };

/** Einheitliche Listenzeile (immer eine Zeile, gleiche Höhe): links Zeit/Datum, Titel (Medium), Zusatz (grau), rechts Angaben. */
function zeile({ links = '', titel = '', neben = '', rechts = '', klasse = '', attr = '', stil = '', punkt = false }) {
  return `<li class="ub-z ${klasse}"${attr ? ` ${attr}` : ''}${stil ? ` style="${stil}"` : ''}>
    <span class="ub-z-links">${links}</span>${punkt ? '<span class="ub-z-fach" aria-hidden="true"></span>' : ''}
    <span class="ub-z-haupt"><span class="ub-z-titel">${titel}</span>${neben ? `<span class="ub-z-neben">${neben}</span>` : ''}</span>
    ${rechts ? `<span class="ub-z-rechts">${rechts}</span>` : ''}</li>`;
}

/**
 * Gruppe einer Karte: Überschrift (mit Anzahl), höchstens fünf Zeilen und „Alle anzeigen“ in der Überschrift.
 * Ohne Zeilen liefert sie nur ihren Leertext für die gemeinsame graue Zeile der Karte.
 */
function gruppe({ titel, zeilen, href = '', leer = '', zahl = null, mehrHref = href, attr = '' }) {
  if (!zeilen.length) return { leer };
  const n = zahl ?? zeilen.length;
  const mehr = zeilen.length > MAX && mehrHref ? `<a class="ub-alle" href="${mehrHref}">Alle anzeigen <span>(${anzahl(zeilen.length)})</span></a>` : '';
  const kopf = href ? `<a href="${href}">${e(titel)}</a>` : e(titel);
  return {
    html: `<div class="ub-gruppe"><h3>${kopf}${n ? `<span class="ub-anz">${anzahl(n)}</span>` : ''}${mehr}</h3>
      <ul class="ub-liste"${attr ? ` ${attr}` : ''}>${zeilen.slice(0, MAX).join('')}</ul></div>`,
  };
}

/** Graue Sammelzeile für alle leeren Gruppen („Keine Prüfungen · keine Aufsichten“). */
function ruhig(texte, symbol = 'check2') {
  const t = texte.filter(Boolean);
  if (!t.length) return '';
  const satz = t.map((x, i) => i ? x.charAt(0).toLowerCase() + x.slice(1) : x).join(' · ');
  return `<p class="ub-ruhig"><i class="bi bi-${symbol}" aria-hidden="true"></i><span>${e(satz)}</span></p>`;
}

/** Karte mit neutralem Kopf (Symbol im Akzent, dunkelgrauer Titel, Link rechts) und Gruppen. */
function karte({ klasse, symbol, titel, href = '', linkText = '', gruppen = [], zusatz = '', zusatzVorne = false, leerSymbol = 'check2' }) {
  const voll = gruppen.filter(g => g.html);
  const leer = gruppen.filter(g => !g.html).map(g => g.leer);
  return `<section class="ub-karte ${klasse}" aria-label="${e(titel)}">
    <header><i class="bi bi-${symbol}" aria-hidden="true"></i><h2>${e(titel)}</h2>
      ${href ? `<a class="ub-link" href="${href}">${e(linkText || 'Öffnen')}<i class="bi bi-chevron-right" aria-hidden="true"></i></a>` : ''}</header>
    <div class="ub-koerper">${zusatzVorne ? zusatz : ''}${voll.map(g => g.html).join('')}${zusatzVorne ? '' : zusatz}${ruhig(leer, leerSymbol)}</div></section>`;
}

/** Kennzahl der Leiste: Zahl und Bezeichnung; 0 bleibt zurückhaltend grau. */
const kennzahl = ({ zahl, titel, href }) => `<a class="ub-kz ${zahl > 0 ? 'an' : ''}" href="${href}"><b>${anzahl(zahl)}</b><span>${e(titel)}</span></a>`;

/** Erster freier Tag (Ferien/Feiertag/unterrichtsfrei) ab heute mit Name und Zeitraum – für Tageszeile und Hinweis. */
function ferienAb(tage, heute) {
  const t = tage.find(x => x.frei && x.datum >= heute && (x.freiArt === 'ferien' || x.freiArt === 'feiertag' || x.freiArt === 'frei'));
  if (!t) return null;
  const von = t.freiVon || t.datum, bis = t.freiBis || von;
  const name = t.freiGrund || ({ ferien: 'Ferien', feiertag: 'Feiertag' }[t.freiArt] ?? 'Unterrichtsfrei');
  const laeuft = von <= heute;
  const text = laeuft ? `${name} bis ${tagKurz(bis)}` : von === bis ? `${name} am ${tagKurz(von)}` : `${name} ${tagKurz(von)} – ${tagKurz(bis)}`;
  return { text, laeuft, von, bis };
}

export async function zeigen(seite, kontext) {
  stileLaden();
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
  const fehlzeiten = kollegium && ich.fehlzeiten;
  const [w1, w2, pruefungen, hausaufgaben, offen, mitteilungen, abwesenheiten, info, fam, sprechtage] = await Promise.all([
    plan(mo), plan(plusTage(mo, 7)),
    holen(`/api/app/pruefungen?von=${heute}&bis=${plusTage(heute, 28)}${filter}`).catch(() => []),
    familie || !element ? [] : holen(`/api/app/hausaufgaben?von=${plusTage(heute, -7)}&bis=${plusTage(heute, 28)}${filter}`).catch(() => []),
    kollegium && element ? holen('/api/app/klassenbuch/offen').catch(() => []) : [],
    holen('/api/app/mitteilungen?ordner=eingang').catch(() => []),
    fehlzeiten ? holen(`/api/app/abwesenheiten/zeitraeume?von=${plusTage(heute, -30)}&bis=${heute}&status=offen`).catch(() => []) : [],
    holen('/api/app/infocenter').catch(() => ({ eintraege: [] })),
    familie ? holen(`/api/app/familie/uebersicht?schueler=${kind.id}`).catch(() => ({ offeneAbwesenheiten: [], hausaufgaben: [] })) : null,
    holen('/api/app/sprechtag/start').catch(() => []),
  ]);

  // ---------------------------------------------------------------- Daten aufbereiten
  const plaene = [w1, w2];
  // in den Ferien: zusätzlich die Woche nach Ferienende laden, damit der nächste Unterrichtstag erscheint
  const ferienVorab = element ? ferienAb([...(w1.tage ?? []), ...(w2.tage ?? [])].sort((a, b) => a.datum.localeCompare(b.datum)), heute) : null;
  if (ferienVorab && ferienVorab.bis >= plusTage(mo, 13) && ![...(w1.karten ?? []), ...(w2.karten ?? [])].some(k => k.datum >= heute))
    plaene.push(await plan(montag(plusTage(ferienVorab.bis, 3))));
  const alleKarten = plaene.flatMap(p => p.karten ?? []);
  const alleTage = plaene.flatMap(p => p.tage ?? []).sort((a, b) => a.datum.localeCompare(b.datum));
  const kartenAm = d => alleKarten.filter(k => k.datum === d).sort((a, b) => a.von - b.von);
  // „Heute & nächste Tage“: höchstens zwei Unterrichtstage ab heute (heute zählt, solange dort Stunden stehen)
  const unterrichtstage = alleTage.filter(t => t.datum >= heute && !t.frei && kartenAm(t.datum).length).slice(0, 2).map(t => t.datum);
  const aenderungen = [...(w1.karten ?? []), ...(w2.karten ?? [])].filter(k => k.datum >= heute && !OHNE_AENDERUNG.includes(k.status)).sort((a, b) => a.datum.localeCompare(b.datum) || a.von - b.von);
  const weitere = aenderungen.filter(k => !unterrichtstage.includes(k.datum));
  const ferien = element ? ferienAb(alleTage, heute) : null;
  const gesperrt = [w1, w2].some(p => p.gesperrt) && !alleKarten.length;
  const aufsichten = [...(w1.aufsichten ?? []), ...(w2.aufsichten ?? [])].filter(a => a.datum >= heute && a.datum <= plusTage(heute, 6) && a.status !== 'vertreten' && a.status !== 'entfall')
    .sort((a, b) => a.datum.localeCompare(b.datum) || a.nachStunde - b.nachStunde);
  const offeneHa = familie
    ? (fam.hausaufgaben ?? []).map(h => ({ fach: h.fach, hausaufgabe: h.text, faellig: h.faellig, klassen: [] }))
    : hausaufgaben.filter(h => (h.faellig ?? h.datum) >= heute).sort((a, b) => (a.faellig ?? a.datum).localeCompare(b.faellig ?? b.datum));
  const ungelesen = mitteilungen.filter(m => !m.gelesen);
  const mitteilungenSortiert = [...ungelesen, ...mitteilungen.filter(m => m.gelesen)];
  const infos = (info.eintraege ?? []).filter(x => x.art === 'info');
  const infosNeu = infos.filter(x => !x.gelesen);
  const offeneAbw = familie ? (fam.offeneAbwesenheiten ?? []) : abwesenheiten;
  const zeigeAbw = fehlzeiten || familie;

  // ---------------------------------------------------------------- 1. Kopf mit Tageszeile
  const name = familie ? (ich.rolle === 'eltern' ? (ich.anzeigename || '') : kind.vorname) : (ich.anzeigename || ich.elementName || ich.login || '');
  const tagesteile = [`<span>${e(datumVoll(heute))}</span>`];
  if (familie && ich.rolle === 'eltern' && kindListe.length < 2) tagesteile.push(`<span><b>${e(`${kind.vorname} ${kind.name}`)}</b> · Klasse ${e(kind.klasse)}</span>`);
  if (element && !gesperrt) {
    const heuteStunden = kartenAm(heute).filter(k => !ENTFALL.includes(k.status));
    const jetzt = jetztMinuten();
    const naechste = heuteStunden.find(k => k.ende && minuten(k.ende) > jetzt);
    const folgetag = unterrichtstage.find(d => d > heute);
    const ab = d => { const k = kartenAm(d).find(x => !ENTFALL.includes(x.status)); return k?.beginn ? ` ab ${k.beginn}` : ''; };
    if (naechste) {
      const laeuft = naechste.beginn && minuten(naechste.beginn) <= jetzt;
      const was = [naechste.fach || naechste.text || naechste.art, ...(typ !== 'klasse' ? (naechste.klassen ?? []).map(x => x.k) : []), ...(naechste.raeume ?? []).map(x => x.k)].filter(Boolean).join(' · ');
      tagesteile.push(`<span>${laeuft ? 'Jetzt' : 'Als Nächstes'} <b>${e(naechste.beginn || `${stundenText(naechste.von, naechste.bis)} Std.`)}</b> ${e(was)}</span>`);
    } else if (heuteStunden.length) {
      tagesteile.push(`<span>Unterricht für heute beendet${folgetag ? ` · ${e(tagRel(folgetag, heute))}${e(ab(folgetag))}` : ''}</span>`);
    } else {
      tagesteile.push(`<span><b>Heute kein Unterricht</b>${folgetag ? ` · nächster Unterricht ${e(tagRel(folgetag, heute))}${e(ab(folgetag))}` : ''}</span>`);
    }
  }
  const ferienPille = ferien && (ferien.laeuft || ferien.von <= plusTage(heute, 14))
    ? `<span class="ub-ferien"><i class="bi bi-sun" aria-hidden="true"></i>${e(ferien.text)}</span>` : '';
  const kopf = `<header class="ub-kopf">
      <div class="ub-gruss"><h1>${e(gruss())}${name ? `, ${e(name)}` : ''}</h1>
        <p class="ub-tageszeile">${tagesteile.join('<span class="ub-sep" aria-hidden="true">·</span>')}${ferienPille}</p></div>
      ${familie && kindListe.length > 1 ? `<div class="ub-kind">${kindWahl(kindListe, kind)}</div>` : ''}
    </header>`;

  // ---------------------------------------------------------------- 2. Kennzahlen
  const zahlen = [];
  if (kollegium && element) zahlen.push({ zahl: offen.length, titel: 'Offene Klassenbuch-Einträge', href: '#/offen' });
  if (zeigeAbw) zahlen.push({ zahl: offeneAbw.length, titel: 'Offene Abwesenheiten', href: '#/abwesenheiten' });
  if (element && !kollegium) zahlen.push({ zahl: aenderungen.length, titel: 'Änderungen', href: '#/plan/mein' });
  if (familie) zahlen.push({ zahl: offeneHa.length, titel: 'Hausaufgaben', href: '#/hausaufgaben' });
  zahlen.push({ zahl: pruefungen.length, titel: 'Prüfungen', href: '#/pruefungen' });
  if (!element) zahlen.push({ zahl: infosNeu.length, titel: 'Neue Infos', href: '#/neuigkeiten/infos' });
  zahlen.push({ zahl: ungelesen.length, titel: 'Ungelesene Mitteilungen', href: '#/mitteilungen' });
  const leiste = `<nav class="ub-zahlen" aria-label="Kennzahlen" style="--n:${zahlen.length}">${zahlen.map(kennzahl).join('')}</nav>`;

  // ---------------------------------------------------------------- 3. Karten
  const karten = [];
  const stundenListe = [];
  if (element) {
    const stundeZeile = (k, mitTag) => {
      const i = stundenListe.push(k) - 1;
      const entfall = ENTFALL.includes(k.status);
      const titel = e(k.fach || k.text || k.art || '–');
      const neben = [mitTag ? `${stundenText(k.von, k.bis)} Std.` : '', ...(typ !== 'klasse' ? (k.klassen ?? []).map(x => e(x.alt && entfall ? x.alt : x.k || x.alt)) : []),
        ...(k.raeume ?? []).map(x => e(x.k || x.alt)), k.fach && k.text && !OHNE_AENDERUNG.includes(k.status) ? e(k.text) : ''].filter(Boolean).join(' · ');
      const status = !OHNE_AENDERUNG.includes(k.status) ? `<span class="nl-chip ${chipFarbe(k)}">${e(statusText(k))}</span>` : '';
      const vorbei = !mitTag && k.datum === heute && k.ende && minuten(k.ende) <= jetztMinuten();
      return zeile({
        links: mitTag ? e(tagKurz(k.datum)) : e(k.beginn || `${stundenText(k.von, k.bis)} Std.`), titel, neben, rechts: status, punkt: true, stil: fachStil(k),
        klasse: `ub-stunde${entfall ? ' entfall' : ''}${vorbei ? ' vorbei' : ''}`, attr: `role="button" tabindex="0" data-karte="${i}"`,
      });
    };
    const gruppen = unterrichtstage.map(d => gruppe({
      titel: tagTitel(d, heute), zeilen: kartenAm(d).map(k => stundeZeile(k, false)), href: '#/plan/mein', mehrHref: '#/plan/mein',
      zahl: kartenAm(d).filter(k => !ENTFALL.includes(k.status)).length,
    }));
    gruppen.push(gruppe({ titel: 'Weitere Änderungen', zeilen: weitere.map(k => stundeZeile(k, true)), href: kollegium ? '#/vertretungen' : '#/plan/mein', leer: '' }));
    if (!unterrichtstage.length) gruppen.unshift({ leer: gesperrt ? 'Die Anzeige des Stundenplans ist beschränkt' : ferien?.laeuft ? '' : 'Kein Unterricht in den nächsten Tagen' });
    // Ferienhinweis in der Karte nur, wenn er nicht schon in der Tageszeile steht
    const ferienZeile = ferien && (!ferienPille || !unterrichtstage.length || unterrichtstage[0] > ferien.bis) ? `<p class="ub-hinweis"><i class="bi bi-sun" aria-hidden="true"></i><span>${e(ferien.text)}</span></p>` : '';
    karten.push(karte({ klasse: 'ub-heute', symbol: 'calendar3', titel: 'Heute & nächste Tage', href: '#/plan/mein', linkText: 'Stundenplan', gruppen, zusatz: ferienZeile, zusatzVorne: !!ferien && (!unterrichtstage.length || unterrichtstage[0] > ferien.bis), leerSymbol: gesperrt ? 'lock' : 'calendar-x' }));
  }

  // Zu erledigen: Klassenbuch-Hinweise, offene Abwesenheiten, Hausaufgaben
  const erledigen = [];
  if (kollegium && element)
    erledigen.push(gruppe({
      titel: 'Klassenbuch · Lehrstoff fehlt', href: '#/offen', leer: 'Klassenbuch vollständig',
      zeilen: offen.slice().reverse().map(o => zeile({ links: e(tagKurz(o.datum)), titel: e(o.fach), neben: e((o.klassen || []).join(', ')), rechts: `${stundenText(o.von, o.bis)} Std.` })),
    }));
  if (zeigeAbw)
    erledigen.push(gruppe({
      titel: 'Offene Abwesenheiten', href: '#/abwesenheiten', leer: 'Keine offenen Abwesenheiten',
      zeilen: familie
        // Art wie in „Meine Abwesenheiten“ (verspätet/vorzeitig mit Minuten); Status „offen“ = noch nicht entschieden (nicht „nicht entschuldigt“)
        ? offeneAbw.map(z => zeile({ links: e(tagKurz(z.vonDatum)), titel: z.art === 'verspaetet' ? 'Verspätet' : z.art === 'vorzeitig' ? 'Vorzeitig gegangen' : 'Abwesend',
          neben: z.bisDatum && z.bisDatum !== z.vonDatum ? `bis ${e(tagKurz(z.bisDatum))}` : z.art === 'verspaetet' ? `${e(z.minuten)} min (${e(z.vonZeit)}–${e(z.bisZeit)})` : z.art === 'vorzeitig' ? `ab ${e(z.vonZeit)}` : e(z.vonZeit && z.bisZeit ? `${z.vonZeit}–${z.bisZeit}` : z.vonZeit ? `ab ${z.vonZeit}` : 'ganztägig'),
          rechts: `<span class="nl-chip gelb">${z.statusGemischt ? 'teilweise entschuldigt' : 'offen'}</span>` }))
        : offeneAbw.map(z => zeile({ links: e(tagKurz(z.vonDatum)), titel: e(z.schueler), neben: e(z.klasse), rechts: z.bisDatum && z.bisDatum !== z.vonDatum ? `bis ${e(datumKurz(z.bisDatum))}` : e(z.vonZeit && z.bisZeit ? `${z.vonZeit}–${z.bisZeit}` : '') })),
    }));
  if (element)
    erledigen.push(gruppe({
      titel: familie ? 'Hausaufgaben' : 'Hausaufgaben · fällig', href: '#/hausaufgaben', leer: 'Keine Hausaufgaben',
      zeilen: offeneHa.map(h => zeile({ links: h.faellig ? e(tagKurz(h.faellig)) : '', titel: e(h.fach), neben: e(h.hausaufgabe), rechts: !familie && h.klassen?.length ? e(h.klassen.join(', ')) : '',
        klasse: h.faellig && h.faellig <= plusTage(heute, 1) ? 'bald' : '' })),
    }));
  if (erledigen.length) karten.push(karte({ klasse: 'ub-erledigen', symbol: 'check2-square', titel: 'Zu erledigen', gruppen: erledigen }));

  // Termine: Prüfungen, Aufsichten, Sprechtag
  const termine = [gruppe({
    titel: 'Prüfungen · 4 Wochen', href: '#/pruefungen', leer: 'Keine Prüfungen',
    zeilen: pruefungen.map(p => zeile({ links: e(tagKurz(p.datum)), titel: e(p.titel), neben: e((p.klassen ?? []).join(', ')),
      rechts: `${stundenText(p.stundeVon, p.stundeBis)} Std.${p.raeume?.length ? ` · ${e(p.raeume.join(', '))}` : ''}${p.status === 'vorgemerkt' ? ' <span class="nl-chip grau">vorgemerkt</span>' : ''}` })),
  })];
  if (kollegium && element)
    termine.push(gruppe({
      titel: 'Aufsichten · 7 Tage', href: '#/plan/mein', leer: 'Keine Aufsichten',
      zeilen: aufsichten.map(a => zeile({ links: e(tagKurz(a.datum)), titel: e(a.gangName || a.gang), rechts: `${e(a.beginn)}–${e(a.ende)}${a.status === 'vertretung' ? ' <span class="nl-chip lila">Vertretung</span>' : ''}` })),
    }));
  termine.push(gruppe({
    titel: 'Sprechtag', href: '#/sprechtag', leer: '',
    zeilen: sprechtage.map(s => zeile({ links: s.datum ? e(tagKurz(s.datum)) : '', titel: `<a href="#/sprechtag/${s.id}">${e(s.name)}</a>`, neben: e(s.text || ''), attr: 'data-sprechtag' })),
  }));
  karten.push(karte({ klasse: 'ub-termine', symbol: 'calendar-event', titel: 'Termine', gruppen: termine }));

  // Mitteilungen & Infos
  karten.push(karte({
    klasse: 'ub-mitteilungen', symbol: 'envelope', titel: 'Mitteilungen & Infos', href: '#/mitteilungen', linkText: 'Posteingang',
    gruppen: [
      gruppe({
        titel: 'Mitteilungen', href: '#/mitteilungen', zahl: ungelesen.length, leer: 'Keine Mitteilungen',
        zeilen: mitteilungenSortiert.map(m => zeile({ links: m.erstellt ? e(tagKurz(m.erstellt.slice(0, 10))) : '', titel: `${m.gelesen ? '' : '<span class="ub-neu" aria-label="ungelesen"></span>'}${e(m.absender)}`,
          neben: e(m.betreff || '(ohne Betreff)'), rechts: m.wichtig ? '<span class="nl-chip rot">wichtig</span>' : '', klasse: m.gelesen ? '' : 'ungelesen' })),
      }),
      gruppe({
        titel: 'Infos der Schule', href: '#/neuigkeiten/infos', zahl: infosNeu.length, leer: 'Keine Infos der Schule',
        zeilen: infos.map(x => zeile({ links: x.datum ? e(tagKurz(String(x.datum).slice(0, 10))) : '', titel: `${x.gelesen ? '' : '<span class="ub-neu" aria-label="ungelesen"></span>'}${e(x.titel)}`,
          rechts: x.wichtig ? '<span class="nl-chip rot">wichtig</span>' : '' })),
      }),
    ],
  }));

  seite.innerHTML = `<div class="ub">${kopf}${leiste}
    <div class="ub-raster${karten.length % 2 ? ' ungerade' : ''}">${karten.join('')}</div></div>`;
  const heuteListe = seite.querySelector('.ub-heute');
  if (heuteListe && stundenListe.length) kartenBinden(heuteListe, stundenListe, kontext, typ);
  if (familie) kindBinden(seite, () => kontext.navigieren(location.hash));
}
