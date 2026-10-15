// Heute – Startseite wie im Original (UW-05): „Heute 15.10.2026“, letzte Anmeldung und Planaktualisierung, „Nachrichten“ mit
// RSS-Symbol und Stift (formatierte Tagestexte, sicher bereinigt) und „Mein nächster Unterricht“ als alte Liste mit Auswahl,
// Art, Std., Von, Bis, Klasse, Lehrkraft, Fach, Raum, Symbolen, Klassenbuch, Lehrstoff und Hausaufgaben. Entfall grau
// durchgestrichen, Änderungen als lila Zeile.
import { e, icon, holen, WOCHENTAGE } from '../ui.js';
import { RSS } from '../symbole.js';
import { karteDialog } from '../plangitter.js';

const L = '/img/wu/legacy/';

/** „2026-10-14 17:50:10“ → „Mittwoch, 14.10.2026 17:50:10“ */
function zeitstempel(s) {
  const m = String(s || '').match(/^(\d{4})-(\d{2})-(\d{2})[ T](\d{2}):(\d{2})(?::(\d{2}))?/);
  if (!m) return '---';
  const d = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
  return `${WOCHENTAGE[(d.getDay() + 6) % 7]}, ${m[3]}.${m[2]}.${m[1]} ${m[4]}:${m[5]}:${m[6] ?? '00'}`;
}

const datumDe = iso => iso.split('-').reverse().join('.');
const ERLAUBT = new Set(['B', 'I', 'U', 'BR', 'P', 'SPAN']);

/**
 * Formatierten Tagestext anzeigen: der Server liefert ihn bereits bereinigt (b, i, u, span mit Farbe, br, p); zur Sicherheit
 * wird hier noch einmal nach derselben Whitelist gefiltert – alles andere wird zu Text. Ohne HTML gilt der Klartext.
 */
export function tagestextHtml(n) {
  if (!n.textHtml) return e(n.text);
  return `<span class="h-html">${htmlBereinigt(n)}</span>`;
}

function htmlBereinigt(n) {
  const vorlage = document.createElement('template');
  vorlage.innerHTML = n.textHtml;
  const reinigen = knoten => {
    for (const k of [...knoten.childNodes]) {
      if (k.nodeType === Node.TEXT_NODE) continue;
      if (k.nodeType !== Node.ELEMENT_NODE || !ERLAUBT.has(k.tagName)) { k.replaceWith(document.createTextNode(k.textContent ?? '')); continue; }
      const farbe = k.tagName === 'SPAN' ? (k.getAttribute('style') ?? '').match(/^\s*color\s*:\s*(#[0-9a-f]{3,6}|rgb\(\s*\d{1,3}\s*,\s*\d{1,3}\s*,\s*\d{1,3}\s*\)|[a-z]{3,20})\s*;?\s*$/i)?.[1] : null;
      for (const a of [...k.attributes]) k.removeAttribute(a.name);
      if (farbe) k.style.color = farbe;
      reinigen(k);
    }
  };
  reinigen(vorlage.content);
  const div = document.createElement('div');
  div.append(vorlage.content);
  return div.innerHTML;
}

const wert = l => (l || []).map(w => w.alt && !w.k ? `<s>${e(w.alt)}</s>` : w.alt ? `<span title="${e(w.alt)} → ${e(w.k)}">${e(w.k)}</span>` : e(w.k)).join(', ');
const istEntfall = s => s.status === 'entfall';
const istAenderung = s => ['geaendert', 'offen', 'veranstaltung', 'abwesend', 'klausur', 'buchung'].includes(s.status);

export async function zeigen(seite, kontext) {
  const ich = kontext.ich;
  kontext.titel('');
  const d = await holen('/api/app/start');
  const nachrichten = d.nachrichten.length
    ? d.nachrichten.map(n => !n.betreff ? `<div class="heute-nachricht">${tagestextHtml(n)}</div>`
      : n.aufgefaechert ? `<div class="heute-nachricht"><b class="betreff">${e(n.betreff)}</b>${tagestextHtml(n)}</div>`
        : `<details class="heute-nachricht"><summary>${e(n.betreff)}</summary><div>${tagestextHtml(n)}</div></details>`).join('')
    : '<div class="leer">Keine Tagesnachrichten</div>';

  const stunden = d.stunden ?? [];
  const mitStunden = !d.keinPlan && (stunden.length || ich.kollegium);
  const anderTag = d.tag && d.tag !== d.datum;
  const zeile = (s, i) => {
    const info = [s.text && s.fach ? `Vertretungstext: ${s.text}` : '', s.notizLehrer ? `Notizen für Lehrkräfte: ${s.notizLehrer}` : '', s.notizSchueler ? `Notizen für Schüler*innen: ${s.notizSchueler}` : ''].filter(Boolean).join('\n');
    const mitKb = ich.kollegium && s.unterricht && !istEntfall(s) && s.status !== 'abwesend';
    return `<tr class="${istEntfall(s) ? 'entfall' : istAenderung(s) ? 'aenderung' : ''}" data-i="${i}">
      <td class="mitte"><input type="checkbox" data-wahl aria-label="Stunde ${s.stunde} auswählen"></td>
      <td class="art"><img src="${L}lesson-type.png" alt="" title="${e(s.art || 'Unterricht')}">${istEntfall(s) ? `<img src="${L}delete-red.png" alt="Entfall" title="Entfall" class="x">` : ''}</td>
      <td class="mitte">${s.stunde}</td><td>${e(s.beginn)}</td><td>${e(s.ende)}</td>
      <td>${wert(s.klassen)}</td><td>${wert(s.lehrer)}</td>
      <td>${s.fachAlt ? `<span title="${e(s.fachAlt)} → ${e(s.fach)}">${e(s.fach)}</span>` : e(s.fach || s.text || s.art)}</td><td class="raum">${wert(s.raeume)}</td>
      <td><span class="symbole">${s.online ? '<span class="h-online" title="Findet online statt">@</span>' : ''}${info ? `<img src="${L}info.png" alt="" title="${e(info)}">` : ''}${s.hausaufgabeFaellig ? `<img src="${L}question.png" alt="" title="Hausaufgabe fällig">` : ''}</span></td>
      <td class="mitte">${mitKb ? `<img src="${L}${s.geprueft ? 'absence-sick.png' : 'absence-sick.png'}" alt="" title="${s.geprueft ? 'Abwesenheiten kontrolliert' : 'Abwesenheiten noch nicht kontrolliert'}" class="${s.geprueft ? '' : 'blass'}">` : ''}</td>
      <td class="stoff">${s.unterricht && !istEntfall(s) && s.status !== 'abwesend' && (s.lehrstoff || ich.kollegium) ? `<img src="${L}${s.lehrstoff ? 'lehrstoff.png' : 'lehrstoff-warning.png'}" alt="" title="${s.lehrstoff ? 'Lehrstoff eingetragen' : 'Kein Lehrstoff eingetragen'}"> <span title="${e(s.lehrstoff)}">${e(s.lehrstoff)}</span>` : ''}</td>
      <td class="mitte">${s.hausaufgabe ? `<img src="${L}homework.png" alt="" title="${e(s.hausaufgabe)}">` : ''}</td></tr>`;
  };
  seite.innerHTML = `
    <h1 class="heute-titel">Heute <span>${datumDe(d.datum)}</span></h1>
    <div class="heute">
      <section class="heute-karte heute-anmeldung">
        <div>Letzte Anmeldung: ${e(zeitstempel(d.letzteAnmeldung))}</div>
        <div>Letzte Planaktualisierung aus UntPlan: ${e(zeitstempel(d.planAktualisiert))}</div>
      </section>
      <section class="heute-karte">
        <header>Nachrichten ${d.rss ? `<a href="/rss/nachrichten.xml" target="_blank" rel="noopener" title="RSS-Feed der Nachrichten zum Tag" class="ms-0">${RSS}</a>` : RSS}
          ${ich.nachrichten ? `<a href="#/admin/nachrichten" class="h-stift" title="Nachrichten zum Tag bearbeiten" aria-label="Nachrichten zum Tag bearbeiten"><img src="${L}absence-edit.png" alt=""></a>` : ''}</header>
        <div class="heute-nachrichten">${nachrichten}</div>
      </section>
      ${ich.kollegium && d.offeneStunden ? `<section class="heute-karte heute-hinweis">${icon('offen')}
        <span>${d.offeneStunden} ${d.offeneStunden === 1 ? 'Stunde' : 'Stunden'} der letzten zwei Wochen ohne Lehrstoff.</span>
        <a class="ms-auto fw-bold" href="#/offen">Offene Stunden</a></section>` : ''}
      ${mitStunden ? `<section class="heute-karte">
        <header>${ich.kollegium ? 'Mein nächster Unterricht' : 'Nächster Unterricht'}${anderTag ? ` <span class="h-tag">${e(WOCHENTAGE[(new Date(d.tag + 'T12:00').getDay() + 6) % 7])}, ${datumDe(d.tag)}</span>` : ''}</header>
        ${stunden.length ? `<div class="table-responsive"><table class="heute-liste"><thead><tr>
              <th class="mitte">Auswahl</th><th>Art</th><th class="mitte">Std.</th><th>Von</th><th>Bis</th><th>Klasse</th><th>Lehrkraft</th><th>Fach</th><th>Raum</th><th></th><th>Klassenbuch</th><th>Lehrstoff</th><th>Hausaufgaben</th></tr></thead><tbody>
            ${stunden.map(zeile).join('')}</tbody></table></div>`
          : `<div class="heute-nachrichten"><div class="leer">${d.frei ? `Heute ist unterrichtsfrei (${e(d.frei)}).` : 'In den nächsten Tagen ist kein Unterricht eingetragen.'}</div></div>`}
      </section>` : ''}
      ${d.keinPlan && ich.admin ? `<section class="heute-karte heute-hinweis">${icon('info')}<span>Diesem Administrationskonto ist kein eigener Stundenplan zugewiesen.</span></section>` : ''}
    </div>`;

  // Klick auf eine Stunde öffnet die Stundendetails (Klassenbuch)
  const karten = stunden.map(s => ({
    datum: d.tag ?? d.datum, von: s.von, bis: s.bis, beginn: s.beginn, ende: s.ende, status: s.status, fach: s.fach, fachName: s.fachName, fachAlt: s.fachAlt, farbe: s.farbe,
    klassen: s.klassen, lehrer: s.lehrer, raeume: s.raeume, art: s.art, text: s.text, unterricht: s.unterricht, gruppe: s.gruppe, zusaetzlich: s.zusaetzlich,
    notizSchueler: s.notizSchueler, notizLehrer: s.notizLehrer,
  }));
  seite.querySelectorAll('[data-i]').forEach(tr => tr.onclick = ev => {
    if (ev.target.closest('[data-wahl]')) { tr.classList.toggle('gewaehlt', ev.target.checked); return; }
    const k = karten[Number(tr.dataset.i)];
    karteDialog(k, { ...kontext, liste: karten.filter((x, i, a) => a.findIndex(y => y.von === x.von && y.unterricht === x.unterricht && y.status === x.status) === i) });
  });
}
