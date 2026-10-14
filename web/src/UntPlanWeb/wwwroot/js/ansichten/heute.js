// Startseite: der nächste Unterrichtstag auf einen Blick.
import { e, icon, holen, datumLang, datumKurz, heuteIso, meldungText, statusMarker } from '../ui.js';
import { tagesListe, kartenBinden } from '../plangitter.js';

function begruessung() {
  const h = new Date().getHours();
  return h < 11 ? 'Guten Morgen' : h < 17 ? 'Guten Tag' : 'Guten Abend';
}

export async function zeigen(seite, kontext) {
  const ich = kontext.ich;
  kontext.titel('Heute');
  const d = await holen('/api/app/heute');
  if (!d.paket) {
    seite.innerHTML = `<div class="karte"><div class="leer">${icon('info')}<p><b>Noch keine Daten vorhanden.</b></p>
      <p>Sobald die Schule die Daten aus UntPlan überträgt, erscheinen hier Stundenplan und Vertretungen.</p>
      ${ich.admin ? '<p><a class="knopf haupt" href="#/admin/sync">Synchronisation einrichten</a></p>' : ''}</div></div>`;
    return;
  }
  const istHeute = d.datum === heuteIso();
  const planLink = d.element ? `#/plan/${d.typ}/${encodeURIComponent(d.element)}/${d.datum}` : '#/plan';
  const kennzahlen = [
    { icon: 'vertretung', wert: d.aenderungen, text: d.aenderungen === 1 ? 'Änderung in Ihrem Plan' : 'Änderungen in Ihrem Plan', art: d.aenderungen ? 'rot' : 'gruen', link: planLink },
    { icon: 'mitteilung', wert: d.ungelesen, text: 'ungelesene Mitteilungen', link: '#/mitteilungen' },
  ];
  if (ich.kollegium && d.element) kennzahlen.push({ icon: 'klassenbuch', wert: d.offeneStunden, text: 'Stunden ohne Klassenbucheintrag', link: '#/klassenbuch', art: d.offeneStunden ? '' : 'gruen' });
  if (!ich.kollegium) kennzahlen.push({ icon: 'hausaufgabe', wert: d.hausaufgaben.length, text: 'offene Hausaufgaben', link: '#/klassenbuch' });

  seite.innerHTML = `
    <div class="heute-kopf">
      <div><h2>${begruessung()}, ${e(ich.anzeigename || ich.login)}</h2>
      <div class="gedimmt">${istHeute ? 'Heute' : 'Nächster Unterrichtstag'}: ${e(datumLang(d.datum))}${d.woche ? ` · ${e(d.woche)}-Woche` : ''}</div></div>
    </div>
    <div class="kennzahlen">${kennzahlen.map(k => `<a class="karte kennzahl ${k.art ?? ''}" href="${k.link}" style="color:inherit;text-decoration:none">${icon(k.icon)}<div><b>${k.wert}</b><small>${e(k.text)}</small></div></a>`).join('')}</div>
    ${d.nachrichten.length ? `<div class="karte" style="margin-bottom:18px"><div class="karte-kopf">${icon('info')}<h2>Nachrichten</h2></div>
      <div class="karte-inhalt nachrichtenliste">${d.nachrichten.map(n => `<div class="nachricht">${e(n)}</div>`).join('')}</div></div>` : ''}
    <div class="raster raster-2">
      <div class="karte">
        <div class="karte-kopf">${icon('plan')}<h2>${d.element ? `Mein Tag${d.element ? ' · ' + e(d.element) : ''}` : 'Mein Tag'}</h2>
          <div class="rechts"><a class="knopf klein" href="${planLink}">Woche ansehen ${icon('rechts', 'icon-klein')}</a></div></div>
        <div class="karte-inhalt" data-tag>
          ${d.hinweise.map(h => `<div class="hinweis warnung" style="margin-bottom:10px">${icon('warnung')}<div>${e(h)}</div></div>`).join('')}
          ${!d.element ? '<div class="leer">Ihrem Konto ist keine Lehrkraft oder Klasse zugeordnet.</div>'
            : d.karten.length || d.aufsichten.length ? tagesListe(d.karten, d.aufsichten, d.typ) : '<div class="leer">Kein Unterricht an diesem Tag.</div>'}
        </div>
      </div>
      <div class="raster" style="align-content:start">
        ${d.termine.length ? `<div class="karte"><div class="karte-kopf">${icon('kalender')}<h2>Termine (14 Tage)</h2></div>
          <div class="karte-inhalt"><table class="tabelle"><tbody>${d.termine.map(t => `<tr>
            <td style="white-space:nowrap"><b>${datumKurz(t.von)}</b>${t.bis && t.bis !== t.von ? ' – ' + datumKurz(t.bis) : ''}${t.stundeVon ? `<br><small class="gedimmt">${t.stundeVon}.–${t.stundeBis}. Std.</small>` : ''}</td>
            <td><b>${e(t.titel || t.art)}</b><br><small class="gedimmt">${e(t.art)}${t.klassen?.length ? ' · ' + e(t.klassen.join(', ')) : ''}</small></td></tr>`).join('')}</tbody></table></div></div>` : ''}
        ${!ich.kollegium && d.hausaufgaben.length ? `<div class="karte"><div class="karte-kopf">${icon('hausaufgabe')}<h2>Hausaufgaben</h2></div>
          <div class="karte-inhalt"><table class="tabelle"><tbody>${d.hausaufgaben.map(h => `<tr><td><b>${e(h.fach)}</b><br><small class="gedimmt">${datumKurz(h.datum)}</small></td>
            <td>${e(h.hausaufgabe)}${h.faellig ? `<br><span class="marker petrol">bis ${datumKurz(h.faellig)}</span>` : ''}</td></tr>`).join('')}</tbody></table></div></div>` : ''}
        ${ich.kollegium ? `<div class="karte"><div class="karte-kopf">${icon('melden')}<h2>Meine Meldungen</h2>
            <div class="rechts"><a class="knopf klein" href="#/melden">${icon('plus', 'icon-klein')} Neu</a></div></div>
          <div class="karte-inhalt">${d.meldungen.length ? `<table class="tabelle"><tbody>${d.meldungen.map(m => `<tr><td>${e(meldungText(m))}</td><td style="text-align:right">${statusMarker(m.status)}</td></tr>`).join('')}</tbody></table>`
            : '<div class="leer klein">Keine Meldungen. Abwesenheiten, Raumbuchungen und Klausuren können Sie unter „Melden & Buchen“ eintragen.</div>'}</div></div>` : ''}
      </div>
    </div>`;
  kartenBinden(seite.querySelector('[data-tag]'), d.karten, kontext);
}
