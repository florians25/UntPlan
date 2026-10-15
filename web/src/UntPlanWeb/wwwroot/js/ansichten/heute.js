// Heute – Startseite wie im Original: „Heute 15.10.2026“, Karte mit letzter Anmeldung und letzter Planaktualisierung,
// Karte „Nachrichten“ (Nachrichten zum Tag, RSS) und – mit eigenem Stundenplan – die Stunden des Tages mit Lehrstoff.
import { e, icon, holen, WOCHENTAGE } from '../ui.js';
import { RSS } from '../symbole.js';
import { karteDialog } from '../plangitter.js';

/** „2026-10-14 17:50:10“ → „Mittwoch, 14.10.2026 17:50:10“ */
function zeitstempel(s) {
  const m = String(s || '').match(/^(\d{4})-(\d{2})-(\d{2})[ T](\d{2}):(\d{2})(?::(\d{2}))?/);
  if (!m) return '---';
  const d = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
  return `${WOCHENTAGE[(d.getDay() + 6) % 7]}, ${m[3]}.${m[2]}.${m[1]} ${m[4]}:${m[5]}:${m[6] ?? '00'}`;
}

const datumDe = iso => iso.split('-').reverse().join('.');
const wert = l => (l || []).map(w => w.alt && !w.k ? `<s>${e(w.alt)}</s>` : w.alt || w.neu ? `<span class="wert-neu">${e(w.k)}</span>` : e(w.k)).join(', ');

export async function zeigen(seite, kontext) {
  const ich = kontext.ich;
  kontext.titel('');
  const d = await holen('/api/app/start');
  const nachrichten = d.nachrichten.length
    ? d.nachrichten.map(n => !n.betreff ? `<div class="heute-nachricht">${e(n.text)}</div>`
      : n.aufgefaechert ? `<div class="heute-nachricht"><b>${e(n.betreff)}</b>${e(n.text)}</div>`
        : `<details class="heute-nachricht"><summary>${e(n.betreff)}</summary><div>${e(n.text)}</div></details>`).join('')
    : '<div class="leer">Keine Tagesnachrichten</div>';

  const stunden = d.stunden ?? [];
  const mitStunden = !d.keinPlan && (stunden.length || ich.kollegium);
  seite.innerHTML = `
    <h1 class="heute-titel">Heute <span>${datumDe(d.datum)}</span></h1>
    <div class="heute">
      <section class="heute-karte heute-anmeldung">
        <div>Letzte Anmeldung: ${e(zeitstempel(d.letzteAnmeldung))}</div>
        <div>Letzte Planaktualisierung aus UntPlan: ${e(zeitstempel(d.planAktualisiert))}</div>
      </section>
      <section class="heute-karte">
        <header>Nachrichten ${d.rss ? `<a href="/rss/nachrichten.xml" target="_blank" rel="noopener" title="RSS-Feed der Nachrichten zum Tag" class="ms-0">${RSS}</a>` : RSS}
          ${ich.nachrichten ? '<a href="#/admin/nachrichten">Bearbeiten</a>' : ''}</header>
        <div class="heute-nachrichten">${nachrichten}</div>
      </section>
      ${ich.kollegium && d.offeneStunden ? `<section class="heute-karte heute-hinweis">${icon('offen')}
        <span>${d.offeneStunden} ${d.offeneStunden === 1 ? 'Stunde' : 'Stunden'} der letzten zwei Wochen ohne Lehrstoff.</span>
        <a class="ms-auto fw-bold" href="#/offen">Offene Stunden</a></section>` : ''}
      ${mitStunden ? `<section class="heute-karte">
        <header>${ich.kollegium ? 'Mein Unterricht heute' : 'Unterricht heute'}<a href="#/plan/mein">Mein Stundenplan</a></header>
        ${d.frei ? `<div class="heute-nachrichten"><div class="leer">Heute ist unterrichtsfrei (${e(d.frei)}).</div></div>`
          : stunden.length ? `<div class="table-responsive"><table class="table table-hover heute-tabelle mb-0"><thead><tr>
              <th>Std.</th><th>Von</th><th>Bis</th><th>Klasse</th><th>Lehrkraft</th><th>Fach</th><th>Raum</th><th>Lehrstoff</th><th>Hausaufgaben</th></tr></thead><tbody>
            ${stunden.map((s, i) => `<tr class="${s.status === 'entfall' || s.status === 'abwesend' ? 'entfall' : ''} ${s.status === 'geaendert' || s.status === 'offen' || s.status === 'veranstaltung' ? 'geaendert' : ''}" data-i="${i}" role="button">
              <td class="fw-bold">${s.stunde}.</td><td>${e(s.beginn)}</td><td>${e(s.ende)}</td><td>${wert(s.klassen)}</td><td>${wert(s.lehrer)}</td>
              <td>${s.fachAlt ? `<span class="wert-neu">${e(s.fach)}</span>` : e(s.fach || s.text || s.art)}</td><td>${wert(s.raeume)}</td>
              <td class="stoff" title="${e(s.lehrstoff)}">${e(s.lehrstoff)}</td><td class="stoff" title="${e(s.hausaufgabe)}">${e(s.hausaufgabe)}</td></tr>`).join('')}
            </tbody></table></div>`
            : `<div class="heute-nachrichten"><div class="leer">Heute ist kein Unterricht eingetragen.</div></div>`}
      </section>` : ''}
      ${d.keinPlan && ich.admin ? `<section class="heute-karte heute-hinweis">${icon('info')}<span>Diesem Administrationskonto ist kein eigener Stundenplan zugewiesen.</span></section>` : ''}
    </div>`;

  // Klick auf eine Stunde öffnet die Stundendetails (Klassenbuch)
  const karten = stunden.map(s => ({
    datum: d.datum, von: s.von, bis: s.bis, beginn: s.beginn, ende: s.ende, status: s.status, fach: s.fach, fachName: s.fachName, fachAlt: s.fachAlt, farbe: s.farbe,
    klassen: s.klassen, lehrer: s.lehrer, raeume: s.raeume, art: s.art, text: s.text, unterricht: s.unterricht,
  }));
  seite.querySelectorAll('[data-i]').forEach(tr => tr.onclick = () => {
    const k = karten[Number(tr.dataset.i)];
    karteDialog(k, { ...kontext, liste: karten.filter((x, i, a) => a.findIndex(y => y.von === x.von && y.unterricht === x.unterricht) === i) });
  });
}
