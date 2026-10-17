// Klassenbuch → Offene Stunden im Original-Look (nl-design): oben – direkt auf dem grauen Seitengrund wie bei allen Listenseiten – das gemeinsame
// Suchfeld mit Filter (ui.js suchfeldMitFilter): Chips für Lehrkraft und Klasse (Klassenleitungen bekommen „Meine Klassen“ zuerst), die Art der
// Anzeige als Dropdown im Suchfeld wie bei den Hausaufgaben, rechts der Zeitraum zum Anklicken (Kalender mit zwei Monaten)
// und die Auswahlbox mit vordefinierten Zeitbereichen. Darunter eine schlichte Tabelle ○ | Lehrkraft | Klasse | Fach | Datum | Art | Raum |
// Abwesenheitskontrolle („Prüfen“) | Lehrstoff („Eingeben“ → Seitenleiste mit Vorstunden, Pfeile blättern). Klick auf eine Zeile öffnet die
// Stundendetails. Rechte und der gespeicherte Zeitbereich liegen auf dem Server (/api/app/klassenbuch/offene-stunden).
// Adresse: #/offen/<bereich | ->/<art>/<Lehrkräfte, | ->/<Klasse | ->/<von>/<bis>
import { e, icon, holen, senden, toast, herunterladen, lade, suchfeldMitFilter, suchfeldMitFilterBinden } from '../ui.js';
import { kbDialog as dialog } from '../kbdialog.js';
import { karteDialog } from '../plangitter.js';
import { LEHRFORMEN } from '../stundendetails.js';
import { kalenderOeffnen, offenerKalender } from '../kalender.js';

const ART = {
  offen: 'Offene Stunden',
  kontrolle: 'Abwesenheiten nicht kontrolliert',
  lehrstoff: 'Lehrstoff fehlt',
  alle: 'Alle gehaltenen Stunden',
};
const WT = ['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'];
const datumDe = iso => iso ? iso.split('-').reverse().join('.') : '';
const wochentag = iso => WT[new Date(`${iso}T12:00:00`).getDay()] ?? '';
const istDatum = s => /^\d{4}-\d{2}-\d{2}$/.test(s ?? '');
const passtArt = (art, z) => art === 'kontrolle' ? !z.geprueft : art === 'lehrstoff' ? !z.lehrstoff : art === 'alle' ? true : !z.geprueft || !z.lehrstoff;
const erledigt = text => `<span class="os-status ok" title="${e(text)}"><i class="bi bi-check-lg" aria-hidden="true"></i><span class="visually-hidden">${e(text)}</span></span>`;
const ART_WAHL = [['offen', 'Offen'], ['kontrolle', 'Abwesenheiten fehlen'], ['lehrstoff', 'Lehrstoff fehlt'], ['alle', 'Alle Stunden']];

export async function zeigen(seite, kontext, [bereichParam, artParam, lehrerParam, klasseParam, vonParam, bisParam]) {
  let art = ART[artParam] ? artParam : 'offen';
  const lehrerWahl = lehrerParam && lehrerParam !== '-' ? decodeURIComponent(lehrerParam).split(',').filter(Boolean) : [];
  const klasseWahl = klasseParam && klasseParam !== '-' ? decodeURIComponent(klasseParam) : '';
  const bereichWahl = bereichParam && bereichParam !== '-' ? bereichParam : '';
  const individuell = bereichWahl === 'individuell' && istDatum(vonParam) && istDatum(bisParam);

  kontext.titel('Offene Stunden');
  seite.innerHTML = lade();
  const q = new URLSearchParams({ art: 'alle' });
  if (bereichWahl) q.set('bereich', bereichWahl);
  if (individuell) { q.set('von', vonParam); q.set('bis', bisParam); }
  if (lehrerWahl.length) q.set('lehrer', lehrerWahl.join(','));
  if (klasseWahl) q.set('klasse', klasseWahl);
  const [r, el] = await Promise.all([holen(`/api/app/klassenbuch/offene-stunden?${q}`), kontext.elemente()]);
  // Vorgabe der Klassenbuch-Einstellungen „Offene Stunden anzeigen für“ (Abwesenheitskontrolle, Lehrstoff oder beides)
  if (!ART[artParam] && (r.offenFuer === 'kontrolle' || r.offenFuer === 'lehrstoff')) art = r.offenFuer;

  const zustand = { bereich: r.bereich, art, lehrer: r.lehrer.filter(l => r.alle || lehrerWahl.includes(l)), klasse: r.klasse ?? '', von: r.von, bis: r.bis };
  if (!r.alle) zustand.lehrer = [];
  const ziel = (x = {}) => {
    const z = { ...zustand, ...x };
    const teile = [z.bereich || '-', z.art, encodeURIComponent(z.lehrer.join(',') || '-'), encodeURIComponent(z.klasse || '-')];
    if (z.bereich === 'individuell') teile.push(z.von, z.bis);
    return `#/offen/${teile.join('/')}`;
  };
  const bereichSetzen = async (bereich, von, bis) => {
    try { await senden('/api/app/klassenbuch/offene-stunden/zeitbereich', { bereich, von: von ?? null, bis: bis ?? null }, 'PUT'); }
    catch (err) { toast(err.message, true); }
    kontext.navigieren(ziel({ bereich, von, bis }));
  };

  // ---------------------------------------------------------------- Werkzeugleiste: Suche mit Chips, Zeitraum, Zeitbereich
  const chips = [
    ...zustand.lehrer.map(l => ({ typ: 'lehrer', wert: l, text: l })),
    ...(zustand.klasse ? [{ typ: 'klasse', wert: zustand.klasse, text: `Klasse ${zustand.klasse}` }] : []),
  ];
  // Art der Anzeige: Standard ist die Vorgabe der Klassenbuch-Einstellungen; eine andere Wahl erscheint als Chip im Suchfeld
  const artStandard = r.offenFuer === 'kontrolle' || r.offenFuer === 'lehrstoff' ? r.offenFuer : 'offen';
  seite.innerHTML = `<div class="u-sf-leiste u-kopfteil os-filter">
      ${suchfeldMitFilter({
        platzhalter: r.alle ? 'Lehrkraft, Klasse oder Fach suchen' : 'Klasse oder Fach suchen', label: 'Suche',
        chips: chips.map(c => ({ wert: `${c.typ}:${c.wert}`, text: c.text })),
        filter: [{ name: 'art', label: 'Anzeigen', alle: null, standard: artStandard, wert: art, optionen: ART_WAHL }],
        zusatz: '<div class="os-vorschlaege" id="os-vorschlaege" role="listbox" hidden></div>',
      })}
      <div class="u-sf-rechts os-zeit">
        <button type="button" class="os-zeitraum" data-zeitraum aria-haspopup="dialog" title="Zeitraum wählen"><span>${datumDe(r.von)} - ${datumDe(r.bis)}</span></button>
        <label class="os-bereich"><span class="visually-hidden">Zeitbereich</span>
          <select data-bereich>${r.bereiche.map(b => `<option value="${b.id}" ${b.id === r.bereich ? 'selected' : ''}>${e(b.text)}</option>`).join('')}</select></label>
      </div></div>
    <div data-os-inhalt></div>`;
  const w = seite.querySelector('.os-filter');
  const inhalt = seite.querySelector('[data-os-inhalt]');

  // ---------------------------------------------------------------- Inhalt
  const auswahl = new Set();
  let text = '';
  const nachricht = r.alle || r.klassenleitung.length > 0;
  const zeilen = r.zeilen.map((z, i) => ({ ...z, i }));
  const sichtbar = () => zeilen.filter(z => passtArt(art, z) && (!text || `${z.lehrer} ${z.klassen.join(' ')} ${z.fach} ${z.fachName} ${z.raeume.join(' ')} ${datumDe(z.datum)}`.toLowerCase().includes(text)));
  const zeichnen = () => {
    const liste = sichtbar();
    kontext.titel('Offene Stunden', `${nachricht ? `<button type="button" class="btn btn-outline-secondary" data-nachricht ${liste.length ? '' : 'disabled'}>Nachricht senden</button>` : ''}
      <button type="button" class="btn btn-outline-secondary" data-bericht ${liste.length ? '' : 'disabled'}>Bericht</button>`, `${liste.length}`);
    const alleGewaehlt = liste.length > 0 && liste.every(z => auswahl.has(z.i));
    inhalt.innerHTML = `<div class="os-seite os-original">
      ${liste.length ? `<div class="os-tabelle"><table>
        <thead><tr>
          <th class="os-wahl"><input type="checkbox" class="nl-rund" data-alle ${alleGewaehlt ? 'checked' : ''} aria-label="Alle auswählen"></th>
          <th>Lehrkraft</th><th>Klasse</th><th>Fach</th><th>Datum</th><th>Art</th><th>Raum</th><th>Abwesenheitskontrolle</th><th>Lehrstoff</th></tr></thead>
        <tbody>${liste.map(z => `<tr data-z="${z.i}" tabindex="0" class="${auswahl.has(z.i) ? 'gewaehlt' : ''}" aria-label="${e(`${datumDe(z.datum)}, ${z.von}. Stunde, ${z.fach} ${z.klassen.join(', ')}`)}">
          <td class="os-wahl"><input type="checkbox" class="nl-rund" data-wahl="${z.i}" ${auswahl.has(z.i) ? 'checked' : ''} aria-label="Stunde auswählen"></td>
          <td>${e(z.lehrer)}</td>
          <td>${e(z.klassen.join(', '))}</td>
          <td>${e(z.fach)}</td>
          <td class="text-nowrap os-datum">${datumDe(z.datum)} ${e(z.beginn)} - ${e(z.ende)}</td>
          <td class="os-artspalte">${e(z.art || '')}</td>
          <td>${e(z.raeume.join(', '))}</td>
          <td>${z.geprueft ? erledigt('Abwesenheiten kontrolliert') : `<button type="button" class="os-pille" data-pruefen="${z.i}">Prüfen</button>`}</td>
          <td>${z.lehrstoff ? erledigt('Lehrstoff eingetragen') : `<button type="button" class="os-pille" data-eingeben="${z.i}">Eingeben</button>`}</td></tr>`).join('')}</tbody></table>
        ${auswahl.size ? `<div class="os-fuss">${auswahl.size} ${auswahl.size === 1 ? 'Stunde' : 'Stunden'} ausgewählt<button type="button" class="nl-knopf" data-auswahl-weg>Auswahl aufheben</button></div>` : ''}</div>`
        : `<div class="s-leer"><span class="s-leer-symbol"><i class="bi bi-check2-circle"></i></span><b>${r.gesamt ? 'Alles erledigt' : 'Keine gehaltenen Stunden'}</b>
            <p>${r.gesamt ? (text ? 'Keine Stunde passt zur Suche.' : 'Im gewählten Zeitraum sind Abwesenheiten und Lehrstoff vollständig eingetragen.') : 'Im gewählten Zeitraum gibt es keine gehaltenen Stunden.'}</p></div>`}
      ${!r.alle && r.klassenleitung.length && !zustand.klasse ? `<p class="os-hinweis">${icon('info')}Als Klassenleitung können Sie über die Suche alle Stunden ${r.klassenleitung.length === 1 ? 'Ihrer Klasse' : 'Ihrer Klassen'} anzeigen.</p>` : ''}
    </div>`;
    binden(liste);
  };

  // Stundendetails: Liste = sichtbare Zeilen (Pfeile blättern durch die offenen Stunden)
  const karte = z => ({ datum: z.datum, von: z.von, bis: z.bis, beginn: z.beginn, ende: z.ende, status: z.status === 'klausur' ? 'regulaer' : z.status, fach: z.fach, fachName: z.fachName,
    farbe: z.farbe, klassen: z.klassen.map(k => ({ k })), lehrer: [{ k: z.lehrer }], raeume: z.raeume.map(k => ({ k })), art: z.art, text: '', unterricht: z.unterricht });

  const binden = liste => {
    const karten = liste.map(karte);
    const oeffnen = tr => { const i = liste.findIndex(z => z.i === Number(tr.dataset.z)); if (i >= 0) karteDialog(karten[i], { ...kontext, liste: karten, nachSpeichern: () => kontext.navigieren(location.hash) }); };
    seite.querySelectorAll('tr[data-z]').forEach(tr => {
      tr.onclick = ev => { if (!ev.target.closest('.os-wahl, [data-eingeben]')) oeffnen(tr); };
      tr.onkeydown = ev => { if ((ev.key === 'Enter' || ev.key === ' ') && ev.target === tr) { ev.preventDefault(); oeffnen(tr); } };
    });
    seite.querySelectorAll('[data-wahl]').forEach(c => c.onchange = () => { const i = Number(c.dataset.wahl); if (c.checked) auswahl.add(i); else auswahl.delete(i); zeichnen(); });
    seite.querySelector('[data-alle]')?.addEventListener('change', ev => { liste.forEach(z => ev.target.checked ? auswahl.add(z.i) : auswahl.delete(z.i)); zeichnen(); });
    seite.querySelector('[data-auswahl-weg]')?.addEventListener('click', () => { auswahl.clear(); zeichnen(); });
    seite.querySelectorAll('[data-eingeben]').forEach(b => b.onclick = ev => { ev.stopPropagation(); lehrstoffLeiste(liste, liste.findIndex(z => z.i === Number(b.dataset.eingeben))); });
    const gewaehlt = () => auswahl.size ? zeilen.filter(z => auswahl.has(z.i)) : liste;
    document.querySelector('[data-nachricht]')?.addEventListener('click', () => nachrichtDialog(gewaehlt()));
    document.querySelector('[data-bericht]')?.addEventListener('click', () => herunterladen(`offene-stunden-${r.von}-${r.bis}.csv`,
      ['Datum;Beginn;Ende;Stunde;Klasse;Fach;Lehrkraft;Art;Raum;Abwesenheiten kontrolliert;Lehrstoff',
        ...gewaehlt().map(z => [datumDe(z.datum), z.beginn, z.ende, z.von === z.bis ? z.von : `${z.von}-${z.bis}`, z.klassen.join(', '), z.fach, z.lehrer, z.art, z.raeume.join(', '),
          z.geprueft ? 'ja' : 'nein', z.lehrstoff ? 'ja' : 'nein'].map(x => `"${String(x ?? '').replace(/"/g, '""')}"`).join(';'))].join('\r\n')));
  };

  // Nachricht an die betroffenen Lehrkräfte (Empfänger vorausgefüllt)
  async function nachrichtDialog(liste) {
    const empfaenger = await holen('/api/app/mitteilungen/empfaenger');
    const kuerzel = [...new Set(liste.map(z => z.lehrer))];
    const ziele = empfaenger.filter(x => x.id.startsWith('benutzer:') && kuerzel.some(k => x.name.toLowerCase().includes(`(${k.toLowerCase()})`) || x.name.toLowerCase().includes(k.toLowerCase())));
    const nachricht = `Bitte tragen Sie für folgende Stunden die Abwesenheitskontrolle bzw. den Lehrstoff nach:\n\n${liste.slice(0, 60).map(z =>
      `- ${datumDe(z.datum)} ${z.beginn}–${z.ende} ${z.klassen.join(', ')} ${z.fach} (${z.lehrer})${!z.geprueft ? ' · Abwesenheiten' : ''}${!z.lehrstoff ? ' · Lehrstoff' : ''}`).join('\n')}${liste.length > 60 ? `\n… und ${liste.length - 60} weitere` : ''}`;
    dialog({
      titel: 'Nachricht senden', breit: true,
      inhalt: `<div class="mb-3"><label class="form-label">Empfänger</label><div class="mt-empfaenger">${ziele.length ? ziele.map(x => `<span class="u-chip">${e(x.name)}</span>`).join('') : '<span class="text-body-secondary small">Für die betroffenen Lehrkräfte gibt es kein Benutzerkonto.</span>'}</div></div>
        <div class="mb-3"><label class="form-label">Betreff</label><input class="form-control" name="betreff" value="Offene Stunden im Klassenbuch"></div>
        <div><label class="form-label">Nachricht</label><textarea class="form-control" name="text" rows="8">${e(nachricht)}</textarea></div>`,
      knoepfe: [{ text: 'Abbrechen' }, { text: 'Nachricht senden', art: 'haupt', aktion: async box => {
        if (!ziele.length) { toast('Keine Empfänger vorhanden.', true); return false; }
        await senden('/api/app/mitteilungen', { empfaenger: ziele.map(x => x.id), betreff: box.querySelector('[name=betreff]').value, text: box.querySelector('[name=text]').value, wichtig: false });
        toast('Nachricht gesendet.');
      } }],
    });
  }

  // ---------------------------------------------------------------- Suche: Vorschläge (Meine Klassen zuerst), freie Eingabe filtert die Liste
  const suche = w.querySelector('[data-suche]');
  const vorschlaege = w.querySelector('#os-vorschlaege');
  const gruppen = () => {
    const t = suche.value.trim().toLowerCase();
    const passt = (...x) => !t || x.some(y => (y ?? '').toLowerCase().includes(t));
    const g = [];
    const meine = r.klassenleitung.filter(k => passt(k) && k !== zustand.klasse);
    if (meine.length) g.push(['Meine Klassen', meine.map(k => ({ typ: 'klasse', wert: k, text: k, info: 'alle Stunden der Klasse' }))]);
    const klassen = el.klassen.filter(k => !r.klassenleitung.includes(k.kurz) && k.kurz !== zustand.klasse && passt(k.kurz, k.name)).slice(0, t ? 12 : 6)
      .map(k => ({ typ: 'klasse', wert: k.kurz, text: k.kurz, info: k.name && k.name !== k.kurz ? k.name : '' }));
    if (klassen.length) g.push(['Klassen', klassen]);
    if (r.alle) {
      const lehrer = el.lehrer.filter(l => !zustand.lehrer.includes(l.kurz) && passt(l.kurz, l.name)).slice(0, t ? 12 : 8).map(l => ({ typ: 'lehrer', wert: l.kurz, text: l.kurz, info: l.name }));
      if (lehrer.length) g.push(['Lehrkräfte', lehrer]);
    }
    return g;
  };
  let markiert = -1;
  const vorschlagListe = () => [...vorschlaege.querySelectorAll('[data-vorschlag]')];
  const zeigeVorschlaege = () => {
    const g = gruppen();
    markiert = -1;
    vorschlaege.innerHTML = g.map(([titel, eintraege]) => `<div class="os-v-titel">${e(titel)}</div>${eintraege.map(x =>
      `<button type="button" role="option" data-vorschlag="${x.typ}:${e(x.wert)}"><b>${e(x.text)}</b>${x.info ? `<small>${e(x.info)}</small>` : ''}</button>`).join('')}`).join('');
    vorschlaege.hidden = !g.length;
    suche.setAttribute('aria-expanded', String(!vorschlaege.hidden));
    vorschlagListe().forEach(b => b.onmousedown = ev => { ev.preventDefault(); waehlen(b.dataset.vorschlag); });
  };
  const waehlen = wert => {
    const [typ, ...rest] = wert.split(':');
    const v = rest.join(':');
    if (typ === 'klasse') kontext.navigieren(ziel({ klasse: v }));
    else if (typ === 'lehrer') kontext.navigieren(ziel({ lehrer: [...new Set([...zustand.lehrer, v])] }));
    else if (typ === 'art') kontext.navigieren(ziel({ art: v }));
  };
  suche.onfocus = zeigeVorschlaege;
  suche.oninput = () => { text = suche.value.trim().toLowerCase(); zeigeVorschlaege(); zeichnen(); };
  suche.onblur = () => setTimeout(() => { vorschlaege.hidden = true; suche.setAttribute('aria-expanded', 'false'); }, 120);
  suche.onkeydown = ev => {
    const l = vorschlagListe();
    if (ev.key === 'ArrowDown' || ev.key === 'ArrowUp') {
      ev.preventDefault();
      if (vorschlaege.hidden) zeigeVorschlaege();
      markiert = Math.max(0, Math.min(l.length - 1, markiert + (ev.key === 'ArrowDown' ? 1 : -1)));
      l.forEach((b, i) => b.classList.toggle('aktiv', i === markiert));
      l[markiert]?.scrollIntoView({ block: 'nearest' });
    } else if (ev.key === 'Enter' && markiert >= 0 && l[markiert]) { ev.preventDefault(); waehlen(l[markiert].dataset.vorschlag); }
    else if (ev.key === 'Escape') { vorschlaege.hidden = true; }
  };
  const chipWeg = (typ, wert) => kontext.navigieren(ziel(typ === 'lehrer' ? { lehrer: zustand.lehrer.filter(l => l !== wert) } : { klasse: '' }));
  // gemeinsames Verhalten: x an den Chips, Rücktaste im leeren Feld, Art-Dropdown (Tastatur über die einheitliche Auswahl)
  suchfeldMitFilterBinden(w, {
    beiChipWeg: c => { const [typ, ...rest] = c.split(':'); chipWeg(typ, rest.join(':')); },
    beiFilter: (name, wert) => { if (name === 'art') kontext.navigieren(ziel({ art: wert })); },
  });

  // ---------------------------------------------------------------- Zeitraum: Klick aufs Datum öffnet einen Kalender mit zwei Monaten
  const zeitKnopf = w.querySelector('[data-zeitraum]');
  zeitKnopf.setAttribute('aria-expanded', 'false');
  zeitKnopf.onclick = ev => {
    if (offenerKalender()?.anker === zeitKnopf) { offenerKalender().schliessen(); return; }
    kalenderOeffnen(zeitKnopf, { modus: 'bereich', von: r.von, bis: r.bis, max: r.heute, monate: innerWidth >= 720 ? 2 : 1, fokusSetzen: ev.detail === 0,
      beiWahl: ({ von, bis }) => bereichSetzen('individuell', von, bis) });
  };
  w.querySelector('[data-bereich]').onchange = ev => {
    if (ev.target.value === 'individuell') { ev.target.value = r.bereich; zeitKnopf.click(); return; }
    bereichSetzen(ev.target.value);
  };

  // ---------------------------------------------------------------- Lehrstoff in der Seitenleiste eintragen (Pfeile: vorige/nächste Stunde der Liste)
  async function lehrstoffLeiste(liste, start) {
    document.querySelector('.os-leiste-huelle')?.remove();
    let i = Math.max(0, start);
    let gespeichert = false;
    const h = document.createElement('div');
    h.className = 'os-leiste-huelle';
    h.innerHTML = `<div class="os-l-schleier" data-zu></div><aside class="os-l-panel" role="dialog" aria-modal="true" aria-labelledby="os-l-titel">
        <header><h2 id="os-l-titel">Lehrstoff</h2><button type="button" class="btn-close" data-zu aria-label="Schließen"></button></header>
        <div class="os-l-rumpf" data-rumpf>${lade()}</div>
        <footer><button type="button" class="btn btn-outline-secondary s-knopf" data-zu>Abbrechen</button><button type="button" class="btn btn-primary s-knopf" data-speichern disabled>Speichern</button></footer></aside>`;
    document.body.append(h);
    requestAnimationFrame(() => h.classList.add('offen'));
    const rumpf = h.querySelector('[data-rumpf]'), knopf = h.querySelector('[data-speichern]');
    const zu = () => { h.classList.remove('offen'); setTimeout(() => h.remove(), 200); document.removeEventListener('keydown', taste); if (gespeichert) kontext.navigieren(location.hash); };
    const taste = ev => { if (ev.key === 'Escape') zu(); };
    document.addEventListener('keydown', taste);
    window.addEventListener('hashchange', () => h.remove(), { once: true });
    h.querySelectorAll('[data-zu]').forEach(x => x.onclick = zu);
    const laden = async () => {
      const z = liste[i];
      knopf.disabled = true;
      rumpf.innerHTML = lade();
      const lk = z.lehrer ? `&lehrer=${encodeURIComponent(z.lehrer)}` : '';
      const [st, vor] = await Promise.all([
        holen(`/api/app/klassenbuch/stunde?datum=${z.datum}&stunde=${z.von}&unterricht=${z.unterricht}${lk}`).catch(() => null),
        holen(`/api/app/klassenbuch/lehrstoff/vorstunden?datum=${z.datum}&stunde=${z.von}&unterricht=${z.unterricht}`).catch(() => []),
      ]);
      const ein = st?.eintrag ?? {};
      const farbe = /^#[0-9a-f]{6}$/i.test(z.farbe || '') ? z.farbe : '#bfc8cf';
      rumpf.innerHTML = `<div class="os-l-stunde">
          <div class="os-l-zeit"><span>${datumDe(z.datum)}</span><span>${e(z.beginn)} - ${e(z.ende)}</span>
            <span class="os-l-pfeile"><button type="button" data-schritt="-1" ${i <= 0 ? 'disabled' : ''} aria-label="Vorherige Stunde"><i class="bi bi-chevron-left"></i></button><button type="button" data-schritt="1" ${i >= liste.length - 1 ? 'disabled' : ''} aria-label="Nächste Stunde"><i class="bi bi-chevron-right"></i></button></span></div>
          <div class="os-l-karte" style="--fach:${farbe}"><b>${e(z.fach)}</b><span>${e(z.klassen.join(', '))}</span><span>${e(z.lehrer)}</span><span>${e(z.raeume.join(', '))}</span></div></div>
        <div class="os-l-feldkopf"><span>Lehrstoff</span><select data-lehrform aria-label="Lehrform"><option value="">Lehrform</option>${LEHRFORMEN.map(l => `<option ${l === ein.lehrform ? 'selected' : ''}>${e(l)}</option>`).join('')}</select></div>
        <textarea class="form-control" rows="4" data-thema maxlength="2000" placeholder="Lehrstoff hier eingeben" ${st?.darf ? '' : 'disabled'}>${e(ein.thema ?? '')}</textarea>
        ${st && !st.darf ? '<p class="os-l-hinweis">Diese Stunde gehört nicht zu Ihrem Unterricht.</p>' : ''}
        <div class="os-l-vor"><div class="os-l-feldkopf"><span>Vorstunden</span></div>
          ${vor.length ? `<ul>${vor.map((v, n) => `<li><span class="os-l-vdatum">${datumDe(v.datum).slice(0, 6)}</span><span class="os-l-vwt">${wochentag(v.datum)}</span><span class="os-l-vtext" title="${e(v.thema)}">${e(v.thema || '–')}</span>
            ${v.thema && st?.darf ? `<button type="button" data-kopie="${n}" title="Lehrstoff übernehmen" aria-label="Lehrstoff übernehmen"><i class="bi bi-copy"></i></button>` : ''}</li>`).join('')}</ul>` : '<p class="os-l-hinweis">Keine Vorstunden</p>'}</div>`;
      const thema = rumpf.querySelector('[data-thema]');
      knopf.disabled = !st?.darf;
      rumpf.querySelectorAll('[data-schritt]').forEach(b => b.onclick = () => { i += Number(b.dataset.schritt); laden(); });
      rumpf.querySelectorAll('[data-kopie]').forEach(b => b.onclick = () => { thema.value = vor[Number(b.dataset.kopie)].thema; thema.focus(); });
      knopf.onclick = async () => {
        if (!thema.value.trim()) { toast('Bitte den Lehrstoff eingeben.', true); return; }
        knopf.disabled = true;
        try {
          await senden('/api/app/klassenbuch/stunde', { datum: z.datum, stunde: z.von, unterricht: z.unterricht, thema: thema.value.trim(), lehrform: rumpf.querySelector('[data-lehrform]').value, lehrer: z.lehrer || null }, 'PUT');
          gespeichert = true;
          z.lehrstoff = true;
          toast('Lehrstoff gespeichert.');
          if (i < liste.length - 1) { i++; laden(); } else zu();
        } catch (err) { toast(err.message, true); knopf.disabled = false; }
      };
      setTimeout(() => thema.focus(), 60);
    };
    laden();
  }

  zeichnen();
}
