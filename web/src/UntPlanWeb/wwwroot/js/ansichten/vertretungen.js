// Vertretungsplan als Liste (Schüler- bzw. Lehrerversion je nach Konto).
import { e, icon, holen, heuteIso, plusTage, datumLang } from '../ui.js';

const ARTEN = { entfall: 'rot', offen: 'rot', klausur: 'gelb', geaendert: 'gruen' };

function alt(wert) { return wert ? `<span class="alt">${e(wert)}</span>` : ''; }

export function tagHtml(t, kollegium) {
  const infos = [
    t.abwesendeKlassen.length ? `<span class="marker gelb">Abwesende Klassen: ${e(t.abwesendeKlassen.join(', '))}</span>` : '',
    kollegium && t.abwesendeLehrer.length ? `<span class="marker">Abwesende Lehrkräfte: ${e(t.abwesendeLehrer.join(', '))}</span>` : '',
    t.blockierteRaeume.length ? `<span class="marker">Nicht verfügbare Räume: ${e(t.blockierteRaeume.join(', '))}</span>` : '',
  ].filter(Boolean).join('');
  return `<section class="vt-tag karte">
    <div class="karte-kopf"><h2>${e(datumLang(t.datum))}</h2>${t.woche ? `<span class="gedimmt">${e(t.woche)}-Woche</span>` : ''}
      <div class="rechts"><span class="marker petrol">${t.zeilen.length} ${t.zeilen.length === 1 ? 'Eintrag' : 'Einträge'}</span></div></div>
    <div class="karte-inhalt">
      ${t.nachrichten.length ? `<div class="nachrichtenliste" style="margin-bottom:12px">${t.nachrichten.map(n => `<div class="nachricht">${icon('info', 'icon-klein')}<div>${e(n)}</div></div>`).join('')}</div>` : ''}
      ${infos ? `<div class="vt-infos">${infos}</div>` : ''}
      ${t.frei ? `<div class="leer">${e(t.freiGrund || 'Unterrichtsfrei')}</div>`
        : !t.zeilen.length ? '<div class="leer">Keine Vertretungen.</div>'
        : `<div class="tabelle-huelle"><table class="tabelle vt-tabelle"><thead><tr>
            <th>Klasse(n)</th><th>Std.</th><th>Fach</th><th>${kollegium ? 'Vertretung' : 'Lehrkraft'}</th><th>Raum</th><th>Art</th><th>Hinweis</th></tr></thead><tbody>
            ${t.zeilen.map(z => `<tr class="vt-zeile ${e(z.status)}" data-klassen="${e(z.klassen.toLowerCase())}">
              <td><b>${e(z.klassen)}</b></td><td>${e(z.stunde)}</td>
              <td>${alt(z.fachAlt)}${e(z.fach)}</td>
              <td>${alt(z.lehrerAlt)}${z.status === 'offen' ? '???' : e(z.lehrer)}</td>
              <td>${alt(z.raumAlt)}${e(z.raum)}</td>
              <td><span class="marker ${ARTEN[z.status] ?? ''}">${e(z.art)}</span></td>
              <td>${e(z.text)}${z.vertrVon ? `<br><small class="gedimmt">Vertr. von ${e(z.vertrVon)}</small>` : ''}${z.nach ? `<br><small class="gedimmt">verlegt nach ${e(z.nach)}</small>` : ''}</td></tr>`).join('')}
          </tbody></table></div>`}
    </div></section>`;
}

export async function zeigen(seite, kontext, [datumParam]) {
  const kollegium = kontext.ich.kollegium;
  const datum = /^\d{4}-\d{2}-\d{2}$/.test(datumParam ?? '') ? datumParam : heuteIso();
  kontext.titel('Vertretungsplan', `<button class="knopf flach rund" data-drucken title="Drucken">${icon('drucken')}</button>`);
  document.querySelector('[data-drucken]').onclick = () => window.print();
  let tage = 2;
  try { tage = Number(localStorage.getItem('untplan:vt-tage') ?? 2) || 2; } catch { /* privat */ }

  seite.innerHTML = `<div class="plan-steuerung">
      ${kollegium ? '<input type="search" data-filter placeholder="Klasse filtern …" style="width:180px" aria-label="Klasse filtern">' : ''}
      <span style="flex:1"></span>
      <div class="segment">${[1, 2, 5].map(n => `<button data-anzahl="${n}" class="${n === tage ? 'aktiv' : ''}">${n === 1 ? '1 Tag' : n + ' Tage'}</button>`).join('')}</div>
      <div class="segment">
        <button data-tage="-1" title="Zurück">${icon('links', 'icon-klein')}</button>
        <button data-tage="0">Heute</button>
        <button data-tage="1" title="Weiter">${icon('rechts', 'icon-klein')}</button>
      </div>
      <input type="date" value="${datum}" data-datum aria-label="Datum" style="width:auto">
    </div><div data-inhalt><div class="lade">Wird geladen …</div></div>`;
  const ziel = d => `#/vertretungen/${d}`;
  seite.querySelectorAll('[data-tage]').forEach(b => b.onclick = () => {
    const n = Number(b.dataset.tage);
    kontext.navigieren(ziel(n === 0 ? heuteIso() : plusTage(datum, n)));
  });
  seite.querySelector('[data-datum]').onchange = ev => { if (ev.target.value) kontext.navigieren(ziel(ev.target.value)); };
  seite.querySelectorAll('[data-anzahl]').forEach(b => b.onclick = () => {
    try { localStorage.setItem('untplan:vt-tage', b.dataset.anzahl); } catch { /* privat */ }
    kontext.navigieren(ziel(datum));
  });

  const istAktuell = kontext.aktuell();
  const r = await holen(`/api/app/vertretungen?datum=${datum}&tage=${tage}`);
  if (!istAktuell()) return;
  const inhalt = seite.querySelector('[data-inhalt]');
  inhalt.innerHTML = r.tage.length ? r.tage.map(t => tagHtml(t, kollegium)).join('') : '<div class="karte"><div class="leer">Keine Unterrichtstage im gewählten Zeitraum.</div></div>';
  seite.querySelector('[data-filter]')?.addEventListener('input', ev => {
    const f = ev.target.value.trim().toLowerCase();
    inhalt.querySelectorAll('.vt-zeile').forEach(z => z.classList.toggle('versteckt', !!f && !z.dataset.klassen.split(', ').some(k => k.startsWith(f))));
  });
}
