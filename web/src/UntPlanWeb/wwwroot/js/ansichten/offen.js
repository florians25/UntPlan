// Klassenbuch → Offene Stunden (nl-design): Werkzeugleiste mit Suche (Filter-Chips für Lehrkraft, Klasse und offene Einträge,
// Klassenleitungen bekommen „Meine Klassen“ zuerst angeboten), Zeitraum zum Anklicken (von/bis) und Auswahlbox mit vordefinierten
// Zeitbereichen. Darunter ruhige Kennzahlen (zugleich Filter) und die Tabelle Datum | Std. | Klasse | Fach | Lehrkraft |
// Abwesenheiten kontrolliert | Lehrstoff. Klick auf eine Zeile öffnet die Stundendetails (Pfeile blättern durch die Liste).
// Rechte und der gespeicherte Zeitbereich liegen auf dem Server (/api/app/klassenbuch/offene-stunden).
// Adresse: #/offen/<bereich | ->/<art>/<Lehrkräfte, | ->/<Klasse | ->/<von>/<bis>
import { e, icon, holen, senden, toast, herunterladen, dialog, lade } from '../ui.js';
import { karteDialog } from '../plangitter.js';

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
const haken = (ok, text) => ok
  ? `<span class="os-status ok" title="${e(text)}: ja"><i class="bi bi-check-lg" aria-hidden="true"></i><span class="visually-hidden">ja</span></span>`
  : `<span class="os-status fehlt" title="${e(text)}: nein"><i class="bi bi-x-lg" aria-hidden="true"></i><span class="visually-hidden">nein</span></span>`;

export async function zeigen(seite, kontext, [bereichParam, artParam, lehrerParam, klasseParam, vonParam, bisParam]) {
  const art = ART[artParam] ? artParam : 'offen';
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
    ...(art === 'kontrolle' || art === 'lehrstoff' || art === 'alle' ? [{ typ: 'art', wert: art, text: ART[art] }] : []),
  ];
  const w = kontext.werkzeug(`<div class="os-leiste">
      <div class="os-suche" data-suchfeld>
        ${icon('suchen')}
        ${chips.map(c => `<span class="os-chip">${e(c.text)}<button type="button" data-chip-weg="${c.typ}:${e(c.wert)}" aria-label="Filter ${e(c.text)} entfernen"><i class="bi bi-x"></i></button></span>`).join('')}
        <input type="search" data-suche autocomplete="off" placeholder="${r.alle ? 'Lehrkraft, Klasse oder Fach suchen' : 'Klasse oder Fach suchen'}" aria-label="Suche" aria-expanded="false" aria-controls="os-vorschlaege">
        ${chips.length ? '<button type="button" class="os-leeren" data-leeren title="Alle Filter entfernen" aria-label="Alle Filter entfernen"><i class="bi bi-x-circle-fill"></i></button>' : ''}
        <div class="os-vorschlaege" id="os-vorschlaege" role="listbox" hidden></div>
      </div>
      <div class="os-zeit">
        <button type="button" class="os-zeitraum" data-zeitraum aria-haspopup="dialog" title="Zeitraum wählen">${icon('kalender')}<span>${datumDe(r.von)} – ${datumDe(r.bis)}</span></button>
        <div class="os-zeitwahl" data-zeitwahl hidden role="dialog" aria-label="Zeitraum wählen">
          <label><span>Von</span><input type="date" data-von value="${r.von}" max="${r.heute}"></label>
          <label><span>Bis</span><input type="date" data-bis value="${r.bis}" max="${r.heute}"></label>
          <div class="os-zeitwahl-fuss"><button type="button" class="nl-knopf" data-zeit-ab>Abbrechen</button><button type="button" class="nl-hinzu" data-zeit-ok>Übernehmen</button></div>
        </div>
        <label class="os-bereich"><span class="visually-hidden">Zeitbereich</span>
          <select data-bereich>${r.bereiche.map(b => `<option value="${b.id}" ${b.id === r.bereich ? 'selected' : ''}>${e(b.text)}</option>`).join('')}</select></label>
      </div></div>`);

  // ---------------------------------------------------------------- Inhalt
  const auswahl = new Set();
  let text = '';
  const nachricht = r.alle || r.klassenleitung.length > 0;
  const zeilen = r.zeilen.map((z, i) => ({ ...z, i }));
  const sichtbar = () => zeilen.filter(z => passtArt(art, z) && (!text || `${z.lehrer} ${z.klassen.join(' ')} ${z.fach} ${z.fachName} ${z.raeume.join(' ')} ${datumDe(z.datum)}`.toLowerCase().includes(text)));
  const zahl = (a, wert, bez, unter = '') => `<button type="button" class="os-zahl ${a === art ? 'an' : ''}" data-art="${a}" aria-pressed="${a === art}">
      <b>${wert}</b><span>${e(bez)}</span>${unter ? `<small>${unter}</small>` : ''}</button>`;

  const zeichnen = () => {
    const liste = sichtbar();
    kontext.titel('Offene Stunden', `${nachricht ? `<button type="button" class="btn btn-outline-secondary" data-nachricht ${liste.length ? '' : 'disabled'}>${icon('mitteilung')}Nachricht senden</button>` : ''}
      <button type="button" class="btn btn-outline-secondary" data-bericht ${liste.length ? '' : 'disabled'}>${icon('herunterladen')}Bericht</button>`, `${liste.length}`);
    const alleGewaehlt = liste.length > 0 && liste.every(z => auswahl.has(z.i));
    seite.innerHTML = `<div class="os-seite">
      <div class="os-zahlen" role="group" aria-label="Kennzahlen und Filter">
        ${zahl('alle', r.gesamt, r.gesamt === 1 ? 'gehaltene Stunde' : 'gehaltene Stunden', `${datumDe(r.von)} – ${datumDe(r.bis)}`)}
        ${zahl('offen', r.offen, 'offen', r.gesamt ? `${r.vollstaendig} % vollständig` : '')}
        ${zahl('kontrolle', r.ohneKontrolle, 'Abwesenheiten nicht kontrolliert')}
        ${zahl('lehrstoff', r.ohneLehrstoff, 'ohne Lehrstoff')}
      </div>
      ${!r.alle ? `<p class="os-hinweis">${icon('info')}${r.klassenleitung.length && !zustand.klasse
        ? `Sie sehen Ihre eigenen Stunden. Als Klassenleitung können Sie über die Suche alle Stunden ${r.klassenleitung.length === 1 ? 'Ihrer Klasse' : 'Ihrer Klassen'} anzeigen.`
        : zustand.klasse && r.klassenleitung.includes(zustand.klasse) ? `Alle gehaltenen Stunden der Klasse ${e(zustand.klasse)}.` : 'Sie sehen die von Ihnen gehaltenen Stunden. Der gewählte Zeitbereich bleibt gespeichert.'}</p>` : ''}
      ${liste.length ? `<div class="os-tabelle"><table>
        <thead><tr>
          <th class="os-wahl"><input type="checkbox" class="nl-rund" data-alle ${alleGewaehlt ? 'checked' : ''} aria-label="Alle auswählen"></th>
          <th>Datum</th><th class="os-std">Std.</th><th>Klasse</th><th>Fach</th><th>Lehrkraft</th>
          <th class="os-mitte">Abwesenheiten kontrolliert</th><th class="os-mitte">Lehrstoff</th><th class="os-pfeil-spalte"></th></tr></thead>
        <tbody>${liste.map(z => `<tr data-z="${z.i}" tabindex="0" class="${auswahl.has(z.i) ? 'gewaehlt' : ''}" aria-label="${e(`${datumDe(z.datum)}, ${z.von}. Stunde, ${z.fach} ${z.klassen.join(', ')}`)}">
          <td class="os-wahl"><input type="checkbox" class="nl-rund" data-wahl="${z.i}" ${auswahl.has(z.i) ? 'checked' : ''} aria-label="Stunde auswählen"></td>
          <td class="text-nowrap"><span class="os-wt">${wochentag(z.datum)}</span> ${datumDe(z.datum)}<small class="os-zeitklein">${e(z.beginn)} – ${e(z.ende)}</small></td>
          <td class="os-std">${z.von === z.bis ? z.von : `${z.von}–${z.bis}`}</td>
          <td>${e(z.klassen.join(', '))}</td>
          <td><span class="os-fach" style="${/^#[0-9a-f]{6}$/i.test(z.farbe || '') ? `--fach:${z.farbe}` : ''}">${e(z.fach)}</span>${z.art ? ` <span class="os-art">${e(z.art)}</span>` : ''}</td>
          <td>${e(z.lehrer)}</td>
          <td class="os-mitte">${haken(z.geprueft, 'Abwesenheiten kontrolliert')}</td>
          <td class="os-mitte">${haken(z.lehrstoff, 'Lehrstoff eingetragen')}</td>
          <td class="os-pfeil-spalte"><i class="bi bi-chevron-right" aria-hidden="true"></i></td></tr>`).join('')}</tbody></table>
        ${auswahl.size ? `<div class="os-fuss">${auswahl.size} ${auswahl.size === 1 ? 'Stunde' : 'Stunden'} ausgewählt<button type="button" class="nl-knopf" data-auswahl-weg>Auswahl aufheben</button></div>` : ''}</div>`
        : `<div class="s-leer"><span class="s-leer-symbol"><i class="bi bi-check2-circle"></i></span><b>${r.gesamt ? 'Alles erledigt' : 'Keine gehaltenen Stunden'}</b>
            <p>${r.gesamt ? (text ? 'Keine Stunde passt zur Suche.' : 'Im gewählten Zeitraum sind Abwesenheiten und Lehrstoff vollständig eingetragen.') : 'Im gewählten Zeitraum gibt es keine gehaltenen Stunden.'}</p></div>`}
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
      tr.onclick = ev => { if (!ev.target.closest('.os-wahl')) oeffnen(tr); };
      tr.onkeydown = ev => { if ((ev.key === 'Enter' || ev.key === ' ') && ev.target === tr) { ev.preventDefault(); oeffnen(tr); } };
    });
    seite.querySelectorAll('[data-wahl]').forEach(c => c.onchange = () => { const i = Number(c.dataset.wahl); if (c.checked) auswahl.add(i); else auswahl.delete(i); zeichnen(); });
    seite.querySelector('[data-alle]')?.addEventListener('change', ev => { liste.forEach(z => ev.target.checked ? auswahl.add(z.i) : auswahl.delete(z.i)); zeichnen(); });
    seite.querySelector('[data-auswahl-weg]')?.addEventListener('click', () => { auswahl.clear(); zeichnen(); });
    seite.querySelectorAll('[data-art]').forEach(b => b.onclick = () => kontext.navigieren(ziel({ art: b.dataset.art === art && art !== 'offen' ? 'offen' : b.dataset.art })));
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
    const filter = ['kontrolle', 'lehrstoff'].filter(a => a !== art && passt(ART[a])).map(a => ({ typ: 'art', wert: a, text: ART[a] }));
    if (filter.length) g.push(['Filter', filter]);
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
    else if (ev.key === 'Backspace' && !suche.value && chips.length) { const c = chips[chips.length - 1]; chipWeg(c.typ, c.wert); }
  };
  const chipWeg = (typ, wert) => kontext.navigieren(ziel(typ === 'lehrer' ? { lehrer: zustand.lehrer.filter(l => l !== wert) } : typ === 'klasse' ? { klasse: '' } : { art: 'offen' }));
  w.querySelectorAll('[data-chip-weg]').forEach(b => b.onclick = () => { const [typ, ...rest] = b.dataset.chipWeg.split(':'); chipWeg(typ, rest.join(':')); });
  w.querySelector('[data-leeren]')?.addEventListener('click', () => kontext.navigieren(ziel({ lehrer: [], klasse: '', art: 'offen' })));
  w.querySelector('[data-suchfeld]').addEventListener('click', ev => { if (!ev.target.closest('button')) suche.focus(); });

  // ---------------------------------------------------------------- Zeitraum: Klick aufs Datum öffnet von/bis, Auswahlbox mit Zeitbereichen
  const zeitwahl = w.querySelector('[data-zeitwahl]');
  const zeitKnopf = w.querySelector('[data-zeitraum]');
  const zeitZu = () => { zeitwahl.hidden = true; zeitKnopf.setAttribute('aria-expanded', 'false'); };
  zeitKnopf.onclick = () => { zeitwahl.hidden = !zeitwahl.hidden; zeitKnopf.setAttribute('aria-expanded', String(!zeitwahl.hidden)); if (!zeitwahl.hidden) zeitwahl.querySelector('[data-von]').focus(); };
  w.querySelector('[data-zeit-ab]').onclick = zeitZu;
  zeitwahl.onkeydown = ev => { if (ev.key === 'Escape') { zeitZu(); zeitKnopf.focus(); } };
  w.querySelector('[data-zeit-ok]').onclick = () => {
    let von = zeitwahl.querySelector('[data-von]').value, bis = zeitwahl.querySelector('[data-bis]').value;
    if (!istDatum(von) || !istDatum(bis)) { toast('Bitte Beginn und Ende wählen.', true); return; }
    if (von > bis) [von, bis] = [bis, von];
    zeitZu();
    bereichSetzen('individuell', von, bis);
  };
  w.querySelector('[data-bereich]').onchange = ev => {
    if (ev.target.value === 'individuell') { ev.target.value = r.bereich; zeitKnopf.click(); return; }
    bereichSetzen(ev.target.value);
  };
  const aussen = ev => { if (!document.body.contains(w) || !w.contains(zeitwahl)) { document.removeEventListener('mousedown', aussen); return; } if (!zeitwahl.hidden && !ev.target.closest('.os-zeit')) zeitZu(); };
  document.addEventListener('mousedown', aussen);

  zeichnen();
}
