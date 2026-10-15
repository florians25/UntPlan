// Klassenbuch → Offene Stunden wie im Original: oben Suche mit Lehrkraft-Chips, Auswahl „Alle“, Zeitraum und „Beginn der Woche“;
// darunter „Offene Stunden 6 / 14“ mit „Mitteilung senden“ und „Bericht“ und die Tabelle mit Abwesenheitskontrolle und Lehrstoff.
import { e, icon, holen, senden, toast, heuteIso, plusTage, montag, herunterladen, leer, dialog } from '../ui.js';
import { karteDialog } from '../plangitter.js';

const BEREICHE = {
  woche: { text: 'Beginn der Woche', von: () => montag(heuteIso()) },
  heute: { text: 'Heute', von: () => heuteIso() },
  letzte: { text: 'Letzte Woche', von: () => plusTage(montag(heuteIso()), -7) },
  tage14: { text: 'Letzte 14 Tage', von: () => plusTage(heuteIso(), -13) },
  monat: { text: 'Aktueller Monat', von: () => heuteIso().slice(0, 8) + '01' },
  jahr: { text: 'Aktuelles Schuljahr', von: null },
};
const ART = { alle: 'Alle', kontrolle: 'Abwesenheitskontrolle', lehrstoff: 'Lehrstoff' };
const datumDe = iso => iso.split('-').reverse().join('.');

export async function zeigen(seite, kontext, [bereichParam, artParam, lehrerParam]) {
  const ich = kontext.ich;
  const leitung = ich.admin || ich.rolle === 'leitung';
  const bereich = BEREICHE[bereichParam] ? bereichParam : 'woche';
  const art = ART[artParam] ? artParam : 'alle';
  const sj = kontext.sitzung.schuljahr;
  const von = bereich === 'jahr' ? (sj?.von ?? plusTage(heuteIso(), -180)) : BEREICHE[bereich].von();
  const bis = heuteIso();
  let lehrer = (lehrerParam && lehrerParam !== '-' ? lehrerParam.split(',') : (ich.element ? [ich.element] : [])).filter(Boolean);
  if (!leitung) lehrer = ich.element ? [ich.element] : [];
  const ziel = (b = bereich, a = art, l = lehrer) => `#/offen/${b}/${a}/${encodeURIComponent(l.join(',') || '-')}`;
  const el = await kontext.elemente();

  const w = kontext.werkzeug(`
    <div class="u-suche">${icon('suchen')}
      ${lehrer.map(l => `<span class="u-chip-dunkel">${e(l)}${leitung ? `<button type="button" data-chip-weg="${e(l)}" aria-label="${e(l)} entfernen"><i class="bi bi-x-lg"></i></button>` : ''}</span>`).join('')}
      <input type="search" data-suche placeholder="${leitung ? 'Lehrkraft, Klasse oder Fach' : 'Klasse oder Fach suchen'}" list="offen-lehrer" aria-label="Suche">
      <button type="button" class="u-leeren" data-leeren title="Filter leeren" aria-label="Filter leeren"><i class="bi bi-x-lg"></i></button>
      <span class="u-trenn"></span>
      <select data-art aria-label="Offene Einträge">${Object.entries(ART).map(([k, t]) => `<option value="${k}" ${k === art ? 'selected' : ''}>${t}</option>`).join('')}</select>
    </div>
    ${leitung ? `<datalist id="offen-lehrer">${el.lehrer.map(l => `<option value="${e(l.kurz)}">${e(l.name)}</option>`).join('')}</datalist>` : ''}
    <div class="u-zeitraum"><span>${datumDe(von)} - ${datumDe(bis)}</span>
      <span class="u-pillenwahl"><select data-bereich aria-label="Zeitraum">${Object.entries(BEREICHE).map(([k, b]) => `<option value="${k}" ${k === bereich ? 'selected' : ''}>${b.text}</option>`).join('')}</select></span></div>`);

  seite.innerHTML = '<div data-inhalt></div>';
  const r = await holen(`/api/app/klassenbuch/stunden?von=${von}&bis=${bis}${lehrer.length ? `&lehrer=${encodeURIComponent(lehrer.join(','))}` : ''}`);
  const offen = r.zeilen.filter(z => art === 'kontrolle' ? !z.geprueft : art === 'lehrstoff' ? !z.lehrstoff : !z.geprueft || !z.lehrstoff);
  const auswahl = new Set();
  const inhalt = seite.querySelector('[data-inhalt]');
  let filter = '';

  const zeichnen = () => {
    const sichtbar = offen.filter(z => !filter || `${z.lehrer} ${z.klassen.join(' ')} ${z.fach} ${z.raeume.join(' ')}`.toLowerCase().includes(filter));
    kontext.titel('Offene Stunden', `<button class="btn btn-outline-secondary" data-mitteilung ${auswahl.size ? '' : 'disabled'}>Mitteilung senden</button>
      <button class="btn btn-outline-secondary" data-bericht>Bericht</button>`, `${sichtbar.length} / ${r.gesamt}`);
    inhalt.innerHTML = sichtbar.length ? `<div class="u-tabelle-karte"><table class="u-tabelle"><thead><tr>
        <th><button type="button" class="u-auswahl ${sichtbar.length && sichtbar.every(z => auswahl.has(z)) ? 'an' : ''}" data-alle aria-label="Alle auswählen"></button></th>
        <th>Lehrkraft</th><th>Klasse</th><th>Fach</th><th>Datum</th><th>Art</th><th>Raum</th><th>Abwesenheitskontrolle</th><th>Lehrstoff</th></tr></thead><tbody>
      ${sichtbar.map(z => { const i = offen.indexOf(z); return `<tr>
        <td><button type="button" class="u-auswahl ${auswahl.has(z) ? 'an' : ''}" data-wahl="${i}" aria-label="Stunde auswählen"></button></td>
        <td>${e(z.lehrer)}</td><td>${e(z.klassen.join(', '))}</td><td>${e(z.fach)}</td>
        <td class="text-nowrap">${datumDe(z.datum)} ${e(z.beginn)} - ${e(z.ende)}</td><td>${e(z.art)}</td><td>${e(z.raeume.join(', '))}</td>
        <td>${z.geprueft ? '<span class="u-statuspille ok">Geprüft</span>' : `<button type="button" class="u-statuspille" data-oeffnen="${i}" data-reiter="klassenbuch">Prüfen</button>`}</td>
        <td>${z.lehrstoff ? '<span class="u-statuspille ok">Erfasst</span>' : `<button type="button" class="u-statuspille" data-oeffnen="${i}" data-reiter="klassenbuch">Eingeben</button>`}</td></tr>`; }).join('')}
      </tbody></table></div>` : `<div class="u-tabelle-karte">${leer(r.gesamt ? 'Alle Stunden im Zeitraum sind vollständig eingetragen.' : 'Im gewählten Zeitraum gibt es keine gehaltenen Stunden.', 'check2-circle')}</div>`;
    inhalt.querySelector('[data-alle]')?.addEventListener('click', () => {
      const alle = sichtbar.every(z => auswahl.has(z));
      sichtbar.forEach(z => alle ? auswahl.delete(z) : auswahl.add(z));
      zeichnen();
    });
    inhalt.querySelectorAll('[data-wahl]').forEach(b => b.onclick = () => { const z = offen[Number(b.dataset.wahl)]; if (auswahl.has(z)) auswahl.delete(z); else auswahl.add(z); zeichnen(); });
    // Stundendetails; „Vorherige/Nächste Stunde“ blättert durch die offenen Stunden
    const karte = z => ({ datum: z.datum, von: z.von, bis: z.bis, beginn: z.beginn, ende: z.ende, status: z.status === 'klausur' ? 'regulaer' : z.status, fach: z.fach, fachName: z.fachName, farbe: z.farbe,
      klassen: z.klassen.map(k => ({ k })), lehrer: [{ k: z.lehrer }], raeume: z.raeume.map(k => ({ k })), art: z.art, text: '', unterricht: z.unterricht });
    const karten = sichtbar.map(karte);
    inhalt.querySelectorAll('[data-oeffnen]').forEach(b => b.onclick = () => {
      const k = karten[sichtbar.indexOf(offen[Number(b.dataset.oeffnen)])];
      karteDialog(k, { ...kontext, liste: karten, nachSpeichern: () => kontext.navigieren(location.hash) });
    });
    document.querySelector('[data-mitteilung]')?.addEventListener('click', () => mitteilung([...auswahl]));
    document.querySelector('[data-bericht]')?.addEventListener('click', () => herunterladen(`offene-stunden-${von}-${bis}.csv`,
      ['Lehrkraft;Klasse;Fach;Datum;Beginn;Ende;Art;Raum;Abwesenheitskontrolle;Lehrstoff',
        ...sichtbar.map(z => [z.lehrer, z.klassen.join(', '), z.fach, datumDe(z.datum), z.beginn, z.ende, z.art, z.raeume.join(', '), z.geprueft ? 'geprüft' : 'offen', z.lehrstoff ? 'erfasst' : 'offen']
          .map(x => `"${String(x ?? '').replace(/"/g, '""')}"`).join(';'))].join('\r\n')));
  };
  zeichnen();

  // Mitteilung an die Lehrkräfte der ausgewählten Stunden (Erinnerung zum Nachtragen)
  async function mitteilung(zeilen) {
    const empfaenger = await holen('/api/app/mitteilungen/empfaenger');
    const kuerzel = [...new Set(zeilen.map(z => z.lehrer))];
    const ziele = empfaenger.filter(x => x.id.startsWith('benutzer:') && kuerzel.some(k => x.name.includes(`(${k.toLowerCase()})`) || x.name.toLowerCase().includes(k.toLowerCase())));
    const text = `Bitte tragen Sie für folgende Stunden Abwesenheitskontrolle und Lehrstoff nach:\n\n${zeilen.map(z => `- ${datumDe(z.datum)} ${z.beginn}–${z.ende} ${z.klassen.join(', ')} ${z.fach}`).join('\n')}`;
    dialog({
      titel: 'Mitteilung senden', breit: true,
      inhalt: `<div class="mb-3"><label class="form-label">Empfänger</label><div class="mt-empfaenger">${ziele.length ? ziele.map(x => `<span class="u-chip">${e(x.name)}</span>`).join('') : '<span class="text-body-secondary small">Für die ausgewählten Lehrkräfte gibt es kein Benutzerkonto.</span>'}</div></div>
        <div class="mb-3"><label class="form-label">Betreff</label><input class="form-control" name="betreff" value="Offene Stunden im Klassenbuch"></div>
        <div><label class="form-label">Nachricht</label><textarea class="form-control" name="text" rows="8">${e(text)}</textarea></div>`,
      knoepfe: [{ text: 'Abbrechen' }, { text: 'Senden', art: 'haupt', aktion: async box => {
        if (!ziele.length) { toast('Keine Empfänger vorhanden.', true); return false; }
        await senden('/api/app/mitteilungen', { empfaenger: ziele.map(x => x.id), betreff: box.querySelector('[name=betreff]').value, text: box.querySelector('[name=text]').value, wichtig: false });
        toast('Mitteilung gesendet.');
      } }],
    });
  }

  // Werkzeugleiste
  w.querySelector('[data-bereich]').onchange = ev => kontext.navigieren(ziel(ev.target.value));
  w.querySelector('[data-art]').onchange = ev => kontext.navigieren(ziel(bereich, ev.target.value));
  w.querySelectorAll('[data-chip-weg]').forEach(b => b.onclick = () => kontext.navigieren(ziel(bereich, art, lehrer.filter(l => l !== b.dataset.chipWeg))));
  w.querySelector('[data-leeren]').onclick = () => { if (leitung && lehrer.length) kontext.navigieren(ziel(bereich, 'alle', [])); else { w.querySelector('[data-suche]').value = ''; filter = ''; zeichnen(); } };
  const suche = w.querySelector('[data-suche]');
  // Eingabe filtert die Liste; Enter mit einem Lehrerkürzel (Schulleitung) übernimmt es als Chip
  suche.oninput = () => { filter = suche.value.trim().toLowerCase(); zeichnen(); };
  suche.onkeydown = ev => {
    const v = suche.value.trim().toUpperCase();
    if (ev.key === 'Enter' && leitung && el.lehrer.some(l => l.kurz.toUpperCase() === v)) { ev.preventDefault(); kontext.navigieren(ziel(bereich, art, [...new Set([...lehrer, el.lehrer.find(l => l.kurz.toUpperCase() === v).kurz])])); }
  };
}
