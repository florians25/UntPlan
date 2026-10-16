// Heute – Startseite wie im Original (UW-05): „Heute 15.10.2026“, letzte Anmeldung und Planaktualisierung, „Nachrichten“ mit
// RSS-Symbol und Stift (formatierte Tagestexte, sicher bereinigt) und „Mein nächster Unterricht“ im neuen Listen-Design
// (nl-tabelle): Uhrzeit, Stunde, Fach, Klasse, Raum, ggf. Lehrkraft, Status-Labels und Klassenbuch-Stand. Entfall grau
// durchgestrichen, Vertretungen mit grünen Labels wie auf den Plankarten.
import { e, icon, holen, WOCHENTAGE, lehrkraftLabel } from '../ui.js';
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

/** Werte (Klassen, Lehrkräfte, Räume) wie auf den Plankarten: geänderter Wert als grünes Label, offene Vertretung durchgestrichen im Label. */
const wert = (l, entfall = false, max = 4, trenn = '→') => {
  const teile = (l || []).map(w => entfall ? e(w.alt || w.k) : lehrkraftLabel(w, undefined, trenn)).filter(Boolean);
  return teile.length > max ? `${teile.slice(0, max - 1).join(', ')} <span class="nl-unter" data-hover="${e((l || []).map(w => w.k || w.alt).join(', '))}" data-hover-art="dunkel">+${teile.length - max + 1}</span>` : teile.join(', ');
};
const istEntfall = s => s.status === 'entfall';

/** Status-Label einer Stunde (Farben wie die übrigen neuen Listen). */
function statusLabels(s) {
  const l = [];
  const chip = (farbe, text, hover = '') => `<span class="nl-chip ${farbe}"${hover ? ` data-hover="${e(hover)}" data-hover-art="dunkel"` : ''}>${text}</span>`;
  if (s.status === 'entfall') l.push(chip('rot', 'Entfall'));
  else if (s.status === 'offen' || s.vertretungOffen) l.push(chip('gelb', 'Vertretung offen'));
  else if (s.status === 'abwesend') l.push(chip('grau', 'vertreten'));
  else if (s.status === 'klausur') l.push(chip('gelb', 'Prüfung'));
  else if (s.status === 'veranstaltung') l.push(chip('grau', 'Veranstaltung'));
  else if (s.status === 'buchung') l.push(chip('grau', 'Raumbuchung'));
  else if (s.status === 'vorgemerkt') l.push(chip('grau', 'vorgemerkt'));
  else if (s.status === 'geaendert') l.push(chip('lila', s.zusaetzlich ? 'Zusätzlich' : 'Änderung', s.art || ''));
  if (s.online && !istEntfall(s)) l.push(chip('gruen', 'Online', 'Findet online statt'));
  return l.join('');
}

export async function zeigen(seite, kontext) {
  const ich = kontext.ich;
  kontext.titel('');
  const d = await holen('/api/app/start');
  const nachrichten = d.nachrichten.length
    ? d.nachrichten.map(n => !n.betreff ? `<div class="heute-nachricht">${tagestextHtml(n)}</div>`
      : n.aufgefaechert ? `<div class="heute-nachricht"><b class="betreff">${e(n.betreff)}</b>${tagestextHtml(n)}</div>`
        : `<details class="heute-nachricht"><summary>${e(n.betreff)}</summary><div>${tagestextHtml(n)}</div></details>`).join('')
    : '<div class="leer">Keine Tagesnachrichten</div>';

  // nach Stunde sortiert (der Server liefert Karten gruppiert, z. B. Veranstaltungen zuerst); Entfall vor der Ersatzstunde
  const stunden = (d.stunden ?? []).slice().sort((a, b) => a.stunde - b.stunde || (a.status === 'entfall' ? 0 : 1) - (b.status === 'entfall' ? 0 : 1));
  // Schüler*innen und Erziehungsberechtigte: nur Anmeldung, Planaktualisierung und Nachrichten (wie im Original)
  const mitStunden = !kontext.familie && !d.keinPlan && (stunden.length || ich.kollegium);
  const anderTag = d.tag && d.tag !== d.datum;
  // Lehrkraft-Spalte nur, wenn sie etwas aussagt (Vertretung, Kopplung, Schüler*innen-Ansicht)
  const mitLehrkraft = !ich.kollegium || stunden.some(s => (s.lehrer || []).length > 1 || (s.lehrer || []).some(w => w.alt || w.neu));
  const zeile = (s, i) => {
    const entfall = istEntfall(s);
    const info = [s.text && s.fach ? `Vertretungstext: ${s.text}` : '', s.notizLehrer ? `Notizen für Lehrkräfte: ${s.notizLehrer}` : '', s.notizSchueler ? `Notizen für Schüler*innen: ${s.notizSchueler}` : ''].filter(Boolean).join('\n');
    const mitKb = ich.kollegium && s.unterricht && !entfall && s.status !== 'abwesend';
    const fach = s.fachAlt && !entfall ? lehrkraftLabel({ k: s.fach, alt: s.fachAlt }, undefined, '→') : e(s.fach || s.text || s.art || '–');
    const kb = [
      mitKb ? (s.geprueft ? '<span class="nl-chip gruen" data-hover="Abwesenheiten kontrolliert" data-hover-art="dunkel"><i class="bi bi-check2" aria-hidden="true"></i>Anwesenheit</span>'
        : '<span class="nl-chip grau" data-hover="Abwesenheiten noch nicht kontrolliert" data-hover-art="dunkel">Anwesenheit offen</span>') : '',
      s.unterricht && !entfall && s.status !== 'abwesend' && s.lehrstoff ? `<span class="hu-stoff" data-hover="${e(s.lehrstoff)}" data-hover-art="dunkel"><i class="bi bi-card-text" aria-hidden="true"></i>${e(s.lehrstoff)}</span>`
        : mitKb ? '<span class="nl-chip gelb">Kein Lehrstoff</span>' : '',
      s.hausaufgabe ? `<span class="nl-chip grau" data-hover="${e(s.hausaufgabe)}" data-hover-art="dunkel"><i class="bi bi-house" aria-hidden="true"></i>Hausaufgabe</span>` : '',
      s.hausaufgabeFaellig ? '<span class="nl-chip lila" data-hover="Zu dieser Stunde ist eine Hausaufgabe fällig" data-hover-art="dunkel">HA fällig</span>' : '',
    ].filter(Boolean).join('');
    return `<tr class="${entfall ? 'entfall' : ''}" data-i="${i}" tabindex="0">
      <td class="hu-zeit"><span class="nl-name">${e(s.beginn)}</span><span class="nl-unter">${e(s.ende)}</span></td>
      <td class="hu-std nl-unter">${s.stunde}.</td>
      <td class="hu-fach"><span class="hu-balken" style="${/^#[0-9a-f]{6}$/i.test(s.farbe || '') ? `--fach:${s.farbe}` : ''}"></span><span class="hu-wert">${fach}</span>${s.fachName && s.fachName !== s.fach && !entfall ? `<span class="nl-unter">${e(s.fachName)}</span>` : ''}</td>
      <td class="hu-wert">${wert(s.klassen, entfall) || '–'}</td>
      <td class="hu-wert">${wert(s.raeume, entfall) || '–'}</td>
      ${mitLehrkraft ? `<td class="hu-wert">${wert(s.lehrer, entfall, 4, '|') || '–'}</td>` : ''}
      <td><span class="nl-chips">${statusLabels(s)}</span></td>
      ${ich.kollegium ? `<td><span class="nl-chips hu-kb">${kb}</span></td>` : ''}
      <td class="hu-info">${info ? `<i class="bi bi-info-circle" data-hover="${e(info)}" data-hover-art="dunkel" aria-label="Info"></i>` : ''}</td></tr>`;
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
        ${stunden.length ? `<div class="nl-tabelle heute-unterricht"><table><thead><tr>
              <th>Uhrzeit</th><th>Std.</th><th>Fach</th><th>Klasse</th><th>Raum</th>${mitLehrkraft ? '<th>Lehrkraft</th>' : ''}<th>Status</th>${ich.kollegium ? '<th>Klassenbuch</th>' : ''}<th><span class="visually-hidden">Info</span></th></tr></thead><tbody>
            ${stunden.map(zeile).join('')}</tbody></table></div>`
          : `<div class="heute-nachrichten"><div class="leer">${d.frei ? `Heute ist unterrichtsfrei (${e(d.frei)}).` : 'In den nächsten Tagen ist kein Unterricht eingetragen.'}</div></div>`}
      </section>` : ''}
      ${d.keinPlan && ich.admin ? `<section class="heute-karte heute-hinweis">${icon('info')}<span>Diesem Administrationskonto ist kein eigener Stundenplan zugewiesen.</span></section>` : ''}
    </div>`;

  // Hinweis „Sprechtag“ (Einladungs- bzw. Anmeldezeitraum) nach der letzten Anmeldung
  import('./sprechtag.js').then(m => m.startkarte(seite.querySelector('.heute'))).catch(() => { /* ohne Hinweis */ });

  // Klick auf eine Stunde öffnet die Stundendetails (Klassenbuch)
  const karten = stunden.map(s => ({
    datum: d.tag ?? d.datum, von: s.von, bis: s.bis, beginn: s.beginn, ende: s.ende, status: s.status, fach: s.fach, fachName: s.fachName, fachAlt: s.fachAlt, farbe: s.farbe,
    klassen: s.klassen, lehrer: s.lehrer, raeume: s.raeume, art: s.art, text: s.text, unterricht: s.unterricht, gruppe: s.gruppe, zusaetzlich: s.zusaetzlich,
    notizSchueler: s.notizSchueler, notizLehrer: s.notizLehrer, lehrerAbwesend: s.lehrerAbwesend, vertretungOffen: s.vertretungOffen,
  }));
  const oeffnen = tr => {
    const k = karten[Number(tr.dataset.i)];
    karteDialog(k, { ...kontext, liste: karten.filter((x, i, a) => a.findIndex(y => y.von === x.von && y.unterricht === x.unterricht && y.status === x.status) === i) });
  };
  seite.querySelectorAll('[data-i]').forEach(tr => {
    tr.onclick = () => oeffnen(tr);
    tr.onkeydown = ev => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); oeffnen(tr); } };
  });
}
