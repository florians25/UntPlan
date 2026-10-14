// Übersicht: Kennzahlen und die Änderungen dieser und der nächsten Woche im eigenen Plan, anstehende Prüfungen und Hausaufgaben.
import { e, icon, holen, heuteIso, plusTage, montag, datumLang, leer, panel, stundenText, datumKachel, datumKurz } from '../ui.js';
import { statusText, werte, gekuerzt, kartenBinden, fachStil } from '../plangitter.js';

const kennzahl = (href, symbol, zahl, text) => `<a class="u-kennzahl" href="${href}">${icon(symbol)}<span><b>${zahl}</b><small>${e(text)}</small></span></a>`;

export async function zeigen(seite, kontext) {
  const ich = kontext.ich;
  kontext.titel('Übersicht');
  const heute = heuteIso();
  const mo = montag(heute);
  const typ = ich.typ, element = ich.element;
  const filter = !element ? '' : typ === 'klasse' ? `&klasse=${encodeURIComponent(element)}` : `&lehrer=${encodeURIComponent(element)}`;
  const leerePlan = { karten: [], tage: [] };
  const [w1, w2, pruefungen, hausaufgaben, offen] = await Promise.all([
    element ? holen(`/api/app/plan?typ=${typ}&element=${encodeURIComponent(element)}&datum=${mo}`).catch(() => leerePlan) : leerePlan,
    element ? holen(`/api/app/plan?typ=${typ}&element=${encodeURIComponent(element)}&datum=${plusTage(mo, 7)}`).catch(() => leerePlan) : leerePlan,
    holen(`/api/app/pruefungen?von=${heute}&bis=${plusTage(heute, 28)}${filter}`).catch(() => []),
    holen(`/api/app/hausaufgaben?von=${plusTage(heute, -7)}&bis=${plusTage(heute, 28)}${filter}`).catch(() => []),
    ich.kollegium && element ? holen('/api/app/klassenbuch/offen').catch(() => []) : [],
  ]);
  const karten = [...w1.karten, ...w2.karten].filter(k => k.datum >= heute && !['regulaer', 'bereitschaft'].includes(k.status));
  const tage = [...w1.tage, ...w2.tage];
  const offeneHa = hausaufgaben.filter(h => (h.faellig ?? h.datum) >= heute);
  const aenderungenWoche = w1.karten.filter(k => !['regulaer', 'bereitschaft'].includes(k.status)).length;

  const nachTag = new Map();
  for (const k of karten) nachTag.set(k.datum, [...(nachTag.get(k.datum) ?? []), k]);
  const ferien = tage.filter(t => t.frei && t.datum >= heute && ['ferien', 'feiertag'].includes(t.freiArt));

  seite.innerHTML = `
    <div class="u-kacheln">
      ${element ? kennzahl(`#/plan/mein/${mo}`, 'vertretungen', aenderungenWoche, 'Änderungen diese Woche') : ''}
      ${kennzahl('#/mitteilungen', 'mitteilungen', kontext.sitzung.ungelesen ?? 0, 'ungelesene Mitteilungen')}
      ${kennzahl('#/pruefungen', 'pruefung', pruefungen.length, 'Prüfungen (4 Wochen)')}
      ${ich.kollegium && element ? kennzahl('#/offen', 'offen', offen.length, 'offene Stunden') : kennzahl('#/hausaufgaben', 'hausaufgaben', offeneHa.length, 'offene Hausaufgaben')}
    </div>
    <div class="row g-3">
      <div class="col-xl-7">
        ${panel('Änderungen in meinem Stundenplan', !element ? leer('Ihrem Konto ist keine Lehrkraft oder Klasse zugeordnet.', 'meinplan')
          : nachTag.size || ferien.length ? `<ul class="u-liste" data-aenderungen>${[...nachTag.entries()].map(([datum, liste]) => `<li>${datumKachel(datum)}
              <div class="u-zeilentext d-flex flex-column gap-1">${liste.map(k => `<div class="d-flex gap-2 align-items-stretch klickbar" role="button" tabindex="0" data-karte="${karten.indexOf(k)}" style="${fachStil(k)};cursor:pointer">
                <span class="u-fachbalken"></span>
                <div class="flex-grow-1"><div class="titel">${stundenText(k.von, k.bis)} Std. · ${e(k.fach || k.text || k.art)}${k.klassen.length && typ !== 'klasse' ? ' · ' + werte(k.klassen) : ''}</div>
                  <div class="unter">${[gekuerzt(k.lehrer, 4), werte(k.raeume)].filter(Boolean).join(' · ')}${k.text && k.fach ? ' · ' + e(k.text) : ''}</div></div>
                <span class="u-chip ${['entfall', 'abwesend'].includes(k.status) ? 'rot' : k.status === 'klausur' ? 'gelb' : 'gruen'} align-self-center">${e(statusText(k))}</span></div>`).join('')}</div></li>`).join('')}
              ${ferien.slice(0, 1).map(t => `<li>${datumKachel(t.datum)}<div class="u-zeilentext"><div class="titel">${icon('schloss')} ${e(t.freiGrund || 'unterrichtsfrei')}</div><div class="unter">${t.freiVon ? `${datumKurz(t.freiVon)} – ${datumKurz(t.freiBis)}` : ''}</div></div></li>`).join('')}</ul>`
          : leer('Keine Änderungen in dieser und der nächsten Woche.', 'meinplan'), { symbol: 'vertretungen', rechts: element ? '<a class="u-mehr" href="#/plan/mein">Mein Stundenplan</a>' : '' })}
      </div>
      <div class="col-xl-5 d-flex flex-column gap-3">
        ${panel('Anstehende Prüfungen', pruefungen.length ? `<ul class="u-liste">${pruefungen.slice(0, 8).map(p => `<li>${datumKachel(p.datum)}
            <div class="u-zeilentext"><div class="titel">${e(p.titel)}</div><div class="unter">${stundenText(p.stundeVon, p.stundeBis)} Std. · ${e(p.klassen.join(', '))}${p.raeume.length ? ' · ' + e(p.raeume.join(', ')) : ''}</div></div>
            ${p.status === 'vorgemerkt' ? '<span class="u-chip">vorgemerkt</span>' : ''}</li>`).join('')}</ul>` : leer('Keine Prüfungen in den nächsten vier Wochen.', 'pruefung'),
          { symbol: 'pruefung', rechts: '<a class="u-mehr" href="#/pruefungen">Alle</a>' })}
        ${panel('Hausaufgaben', offeneHa.length ? `<ul class="u-liste">${offeneHa.slice(0, 8).map(h => `<li>
            <div class="u-zeilentext"><div class="titel">${e(h.fach)} <span class="fw-normal text-body-secondary">· ${e(h.klassen.join(', '))}</span></div><div>${e(h.hausaufgabe)}</div></div>
            ${h.faellig ? `<span class="u-chip ${h.faellig <= plusTage(heute, 1) ? 'orange' : ''}">bis ${datumKurz(h.faellig)}</span>` : ''}</li>`).join('')}</ul>` : leer('Keine offenen Hausaufgaben.', 'hausaufgaben'),
          { symbol: 'hausaufgaben', rechts: '<a class="u-mehr" href="#/hausaufgaben">Alle</a>' })}
      </div>
    </div>
    <p class="small text-body-secondary mt-3">Stand: ${e(datumLang(heute))}</p>`;
  const liste = seite.querySelector('[data-aenderungen]');
  if (liste) kartenBinden(liste, karten, kontext);
}
