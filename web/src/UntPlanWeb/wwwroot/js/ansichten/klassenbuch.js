// Klassenbuch: offene Einträge der Lehrkraft, Verlauf einer Klasse, Hausaufgaben (Klassenkonten).
import { e, icon, holen, datumKurz, datumLang, heuteIso, plusTage } from '../ui.js';
import { karteDialog } from '../plangitter.js';
import { elementWahl } from '../elementwahl.js';

function verlaufTabelle(liste, kollegium) {
  if (!liste.length) return '<div class="leer">Keine Einträge im Zeitraum.</div>';
  return `<div class="tabelle-huelle"><table class="tabelle"><thead><tr><th>Datum</th><th>Std.</th><th>Fach</th>${kollegium ? '<th>Lehrkraft</th>' : ''}<th>Inhalt</th><th>Hausaufgabe</th>${kollegium ? '<th>Notiz</th>' : ''}</tr></thead><tbody>
    ${liste.map(x => `<tr><td style="white-space:nowrap">${e(datumKurz(x.datum))}</td><td>${x.stunde}.</td><td><b>${e(x.fach)}</b></td>${kollegium ? `<td>${e(x.lehrer)}</td>` : ''}
      <td>${e(x.thema)}</td><td>${e(x.hausaufgabe)}${x.faellig ? ` <span class="marker petrol">bis ${datumKurz(x.faellig)}</span>` : ''}</td>${kollegium ? `<td class="gedimmt">${e(x.notiz)}</td>` : ''}</tr>`).join('')}
  </tbody></table></div>`;
}

export async function zeigen(seite, kontext, [klasseParam]) {
  const ich = kontext.ich;
  kontext.titel(ich.kollegium ? 'Klassenbuch' : 'Hausaufgaben');
  const el = await kontext.elemente();
  const klasse = ich.kollegium ? (klasseParam || el.klassen[0]?.kurz) : ich.element;

  if (!ich.kollegium) {
    if (!klasse) { seite.innerHTML = '<div class="karte"><div class="leer">Ihrem Konto ist keine Klasse zugeordnet.</div></div>'; return; }
    const [ha, verlauf] = await Promise.all([
      holen(`/api/app/klassenbuch/hausaufgaben?klasse=${encodeURIComponent(klasse)}`),
      holen(`/api/app/klassenbuch/klasse?klasse=${encodeURIComponent(klasse)}`),
    ]);
    seite.innerHTML = `<div class="raster">
      <div class="karte"><div class="karte-kopf">${icon('hausaufgabe')}<h2>Offene Hausaufgaben · ${e(klasse)}</h2></div>
        <div class="karte-inhalt">${ha.length ? `<table class="tabelle"><thead><tr><th>Fällig</th><th>Fach</th><th>Aufgabe</th><th>Gestellt</th></tr></thead><tbody>
          ${ha.map(x => `<tr><td style="white-space:nowrap"><b>${x.faellig ? e(datumLang(x.faellig)) : '–'}</b></td><td>${e(x.fach)}</td><td>${e(x.hausaufgabe)}</td><td class="gedimmt">${e(datumKurz(x.datum))} · ${e(x.lehrer)}</td></tr>`).join('')}
          </tbody></table>` : '<div class="leer">Keine offenen Hausaufgaben.</div>'}</div></div>
      <div class="karte"><div class="karte-kopf">${icon('klassenbuch')}<h2>Unterrichtsinhalte</h2></div><div class="karte-inhalt">${verlaufTabelle(verlauf, false)}</div></div>
    </div>`;
    return;
  }

  seite.innerHTML = `<div class="raster">
    ${ich.element ? `<div class="karte"><div class="karte-kopf">${icon('warnung')}<h2>Meine Stunden ohne Eintrag (14 Tage)</h2></div><div class="karte-inhalt" data-offen><div class="lade"></div></div></div>` : ''}
    <div class="karte"><div class="karte-kopf">${icon('klassenbuch')}<h2>Klassenbuch</h2><span data-wahl></span>
      <div class="rechts"><input type="date" data-von value="${plusTage(heuteIso(), -28)}" style="width:auto" aria-label="von"> – <input type="date" data-bis value="${plusTage(heuteIso(), 14)}" style="width:auto" aria-label="bis"></div></div>
      <div class="karte-inhalt" data-verlauf><div class="lade"></div></div></div>
  </div>`;
  if (klasse) seite.querySelector('[data-wahl]').replaceWith(elementWahl({ eintraege: el.klassen, wert: klasse, platzhalter: 'Klasse wählen', beiWahl: k => kontext.navigieren(`#/klassenbuch/${encodeURIComponent(k)}`) }));

  const verlaufLaden = async () => {
    const ziel = seite.querySelector('[data-verlauf]');
    if (!klasse) { ziel.innerHTML = '<div class="leer">Keine Klassen vorhanden.</div>'; return; }
    const von = seite.querySelector('[data-von]').value, bis = seite.querySelector('[data-bis]').value;
    const liste = await holen(`/api/app/klassenbuch/klasse?klasse=${encodeURIComponent(klasse)}&von=${von}&bis=${bis}`);
    ziel.innerHTML = verlaufTabelle(liste, true);
  };
  const offenLaden = async () => {
    const ziel = seite.querySelector('[data-offen]');
    if (!ziel) return;
    const offen = await holen('/api/app/klassenbuch/offen');
    ziel.innerHTML = offen.length ? `<div class="raumwahl">${offen.map((o, i) => `<button data-o="${i}"><b>${e(o.fach)}</b> ${e(o.klassen.join(', '))}<small>${e(datumKurz(o.datum))} · ${o.von}. Std.</small></button>`).join('')}</div>`
      : `<div class="leer klein">${icon('haken')}<br>Alles eingetragen.</div>`;
    ziel.querySelectorAll('[data-o]').forEach(b => b.onclick = () => {
      const o = offen[Number(b.dataset.o)];
      karteDialog({ datum: o.datum, von: o.von, bis: o.bis, beginn: '', ende: '', status: 'regulaer', fach: o.fach, fachName: '', klassen: o.klassen.map(k => ({ k })), lehrer: [{ k: ich.element }], raeume: [], art: '', text: '', unterricht: o.unterricht },
        { ...kontext, nachSpeichern: () => { offenLaden(); verlaufLaden(); } });
    });
  };
  seite.querySelector('[data-von]').onchange = verlaufLaden;
  seite.querySelector('[data-bis]').onchange = verlaufLaden;
  await Promise.all([verlaufLaden(), offenLaden()]);
}
