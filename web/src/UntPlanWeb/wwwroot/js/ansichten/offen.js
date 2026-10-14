// Offene Stunden: eigene gehaltene Stunden ohne Lehrstoff; ein Klick öffnet die Stundendetails zum Nachtragen.
import { e, holen, leer, filterFeld, datumKachel, stundenText } from '../ui.js';
import { karteDialog } from '../plangitter.js';

export async function zeigen(seite, kontext, [tageParam]) {
  const ich = kontext.ich;
  const tage = [14, 30, 60, 120].includes(Number(tageParam)) ? Number(tageParam) : 14;
  kontext.titel('Offene Stunden');
  seite.innerHTML = `
    <div class="u-filter">
      ${filterFeld('Zeitraum', `<select class="form-select" data-tage>${[14, 30, 60, 120].map(n => `<option value="${n}" ${n === tage ? 'selected' : ''}>letzte ${n} Tage</option>`).join('')}</select>`)}
      <p class="small text-body-secondary mb-1 ms-md-2">Gehaltene Stunden ohne Lehrstoff. Klicken Sie auf eine Stunde, um Lehrstoff, Hausaufgaben und Anwesenheit nachzutragen.</p>
    </div>
    <div class="card" data-inhalt></div>`;
  seite.querySelector('[data-tage]').onchange = ev => kontext.navigieren(`#/offen/${ev.target.value}`);
  const liste = await holen(`/api/app/klassenbuch/offen?tage=${tage}`);
  const inhalt = seite.querySelector('[data-inhalt]');
  const neu = () => kontext.navigieren(location.hash);
  inhalt.innerHTML = liste.length ? `<div class="card-header d-flex align-items-center">${liste.length} ${liste.length === 1 ? 'offene Stunde' : 'offene Stunden'}</div>
    <ul class="u-liste">${liste.slice().reverse().map((o, i) => `<li class="klickbar" data-o="${liste.length - 1 - i}" style="${/^#[0-9a-f]{6}$/i.test(o.farbe || '') ? `--fach:${o.farbe}` : ''}">
      ${datumKachel(o.datum)}<span class="u-fachbalken"></span>
      <div class="u-zeilentext"><div class="titel">${e(o.fach)} · ${e(o.klassen.join(', '))}</div>
        <div class="unter">${stundenText(o.von, o.bis)} Std.${o.beginn ? ` · ${e(o.beginn)}–${e(o.ende)}` : ''}${o.raeume?.length ? ' · ' + e(o.raeume.join(', ')) : ''}</div></div>
      <span class="u-chip orange align-self-center">kein Lehrstoff</span></li>`).join('')}</ul>`
    : leer('Alle Stunden sind eingetragen.', 'haken');
  inhalt.querySelectorAll('[data-o]').forEach(li => li.onclick = () => {
    const o = liste[Number(li.dataset.o)];
    karteDialog({ datum: o.datum, von: o.von, bis: o.bis, beginn: o.beginn ?? '', ende: o.ende ?? '', status: 'regulaer', fach: o.fach, fachName: o.fachName ?? '', farbe: o.farbe,
      klassen: o.klassen.map(k => ({ k })), lehrer: [{ k: ich.element }], raeume: (o.raeume ?? []).map(k => ({ k })), art: '', text: '', unterricht: o.unterricht }, { ...kontext, nachSpeichern: neu });
  });
}
