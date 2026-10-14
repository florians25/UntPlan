// Meldungen an UntPlan: Abwesenheit melden, Raum buchen, Klausur eintragen, eigene Meldungen verfolgen.
import { e, icon, holen, senden, api, toast, bestaetigen, heuteIso, plusTage, meldungText, statusMarker } from '../ui.js';

const BEREICHE = [
  { id: 'abwesenheit', text: 'Abwesenheit', icon: 'abwesend' },
  { id: 'raum', text: 'Raum buchen', icon: 'raum' },
  { id: 'klausur', text: 'Klausur', icon: 'klausur' },
  { id: 'meine', text: 'Meine Meldungen', icon: 'melden' },
];

function stundenAuswahl(stunden, name, wert) {
  return `<select name="${name}">${stunden.map(s => `<option value="${s.nr}" ${s.nr === wert ? 'selected' : ''}>${e(s.bezeichnung || s.nr + '.')}${s.beginn ? ` (${e(s.beginn)})` : ''}</option>`).join('')}</select>`;
}

function mehrfachAuswahl(name, eintraege) {
  return `<div class="empfaengerwahl" data-mehrfach="${name}" style="max-height:170px">${eintraege.map(x => `<label><input type="checkbox" value="${e(x.kurz)}"> <b>${e(x.kurz)}</b><small>${e(x.name && x.name !== x.kurz ? x.name : '')}</small></label>`).join('')}</div>`;
}
const gewaehlt = (wurzel, name) => [...wurzel.querySelectorAll(`[data-mehrfach="${name}"] input:checked`)].map(x => x.value);

export async function zeigen(seite, kontext, [bereichParam]) {
  const bereich = BEREICHE.some(b => b.id === bereichParam) ? bereichParam : 'abwesenheit';
  kontext.titel('Melden & Buchen');
  const el = await kontext.elemente();
  const stunden = (el.stunden ?? []).map(s => ({ nr: s.nr, bezeichnung: s.bezeichnung, beginn: s.beginn })).filter(s => s.nr > 0);
  if (!stunden.length) for (let i = 1; i <= 10; i++) stunden.push({ nr: i, bezeichnung: `${i}.` });
  const letzte = stunden[stunden.length - 1].nr;

  seite.innerHTML = `<nav class="reiter">${BEREICHE.map(b => `<a href="#/melden/${b.id}" class="${b.id === bereich ? 'aktiv' : ''}">${b.text}</a>`).join('')}</nav><div data-bereich></div>`;
  const ziel = seite.querySelector('[data-bereich]');

  if (bereich === 'abwesenheit') {
    ziel.innerHTML = `<div class="karte" style="max-width:720px"><div class="karte-kopf">${icon('abwesend')}<h2>Abwesenheit melden</h2></div><div class="karte-inhalt">
      <p class="gedimmt">Die Meldung geht an die Vertretungsplanung in UntPlan. Dort wird sie geprüft und als Abwesenheit übernommen.</p>
      <form class="formular">
        <div class="zeile"><label class="feld">von<input type="date" name="von" value="${heuteIso()}" required></label>
          <label class="feld">bis<input type="date" name="bis" value="${heuteIso()}" required></label></div>
        <label class="check"><input type="checkbox" name="ganz" checked> ganztägig</label>
        <div class="zeile versteckt" data-stunden><label class="feld">ab Stunde${stundenAuswahl(stunden, 'stundeVon', 1)}</label>
          <label class="feld">bis Stunde${stundenAuswahl(stunden, 'stundeBis', letzte)}</label></div>
        <label class="feld">Grund<select name="grund"><option>Krankheit</option><option>Fortbildung</option><option>Dienstlicher Termin</option><option>Klassenfahrt / Exkursion</option><option>Sonderurlaub</option><option>Sonstiges</option></select></label>
        <label class="feld">Hinweise für die Vertretung (optional)<textarea name="text" rows="3" placeholder="z. B. Aufgaben für die Klassen, Material im Fach …"></textarea></label>
        <div><button class="knopf haupt" type="submit">${icon('senden', 'icon-klein')} Abwesenheit melden</button></div>
      </form></div></div>`;
    const form = ziel.querySelector('form');
    form.ganz.onchange = () => ziel.querySelector('[data-stunden]').classList.toggle('versteckt', form.ganz.checked);
    form.von.onchange = () => { if (form.bis.value < form.von.value) form.bis.value = form.von.value; };
    form.onsubmit = async ev => {
      ev.preventDefault();
      try {
        await senden('/api/app/meldungen/abwesenheit', {
          von: form.von.value, bis: form.bis.value, grund: form.grund.value, text: form.text.value,
          stundeVon: form.ganz.checked ? null : Number(form.stundeVon.value), stundeBis: form.ganz.checked ? null : Number(form.stundeBis.value),
        });
        toast('Abwesenheit gemeldet. Gute Besserung bzw. viel Erfolg!');
        kontext.navigieren('#/melden/meine');
      } catch (err) { kontext.fehler(err); }
    };
  }

  if (bereich === 'raum') {
    const morgen = plusTage(heuteIso(), 1);
    ziel.innerHTML = `<div class="karte" style="max-width:860px"><div class="karte-kopf">${icon('raum')}<h2>Raum buchen</h2></div><div class="karte-inhalt">
      <form class="formular">
        <div class="zeile"><label class="feld">Datum<input type="date" name="datum" value="${morgen}" min="${heuteIso()}" required></label>
          <label class="feld">von Stunde${stundenAuswahl(stunden, 'stundeVon', 1)}</label>
          <label class="feld">bis Stunde${stundenAuswahl(stunden, 'stundeBis', 1)}</label></div>
        <div><b class="klein">Freie Räume</b><div data-raeume style="margin-top:6px"><div class="lade"></div></div></div>
        <div class="zeile"><label class="feld">Titel<input name="titel" placeholder="z. B. Projektarbeit"></label>
          <label class="feld">Klasse(n) (optional)<input name="klassen" placeholder="z. B. 7a, 7b" list="klassenliste"></label></div>
        <datalist id="klassenliste">${el.klassen.map(k => `<option value="${e(k.kurz)}">`).join('')}</datalist>
        <label class="feld">Bemerkung (optional)<input name="text"></label>
        <div><button class="knopf haupt" type="submit">${icon('haken', 'icon-klein')} Raum buchen</button>
          <span class="gedimmt klein" style="margin-left:8px">Die Buchung erscheint sofort als „vorgemerkt“ im Raumplan und wird von UntPlan übernommen.</span></div>
      </form></div></div>`;
    const form = ziel.querySelector('form');
    let raum = null;
    const laden = async () => {
      if (Number(form.stundeBis.value) < Number(form.stundeVon.value)) form.stundeBis.value = form.stundeVon.value;
      const box = ziel.querySelector('[data-raeume]');
      raum = null;
      try {
        const frei = await holen(`/api/app/meldungen/freieraeume?datum=${form.datum.value}&von=${form.stundeVon.value}&bis=${form.stundeBis.value}`);
        box.innerHTML = frei.length ? `<div class="raumwahl">${frei.map(r => `<button type="button" data-raum="${e(r.kurz)}"><b>${e(r.kurz)}</b><small>${e(r.name && r.name !== r.kurz ? r.name : ' ')}</small></button>`).join('')}</div>`
          : '<div class="hinweis warnung">Kein Raum frei.</div>';
        box.querySelectorAll('[data-raum]').forEach(b => b.onclick = () => {
          raum = b.dataset.raum;
          box.querySelectorAll('[data-raum]').forEach(x => x.classList.toggle('aktiv', x === b));
        });
      } catch (err) { box.innerHTML = `<div class="hinweis fehler">${e(err.message)}</div>`; }
    };
    form.datum.onchange = form.stundeVon.onchange = form.stundeBis.onchange = laden;
    laden();
    form.onsubmit = async ev => {
      ev.preventDefault();
      if (!raum) { toast('Bitte einen freien Raum auswählen.', true); return; }
      try {
        await senden('/api/app/meldungen/raumbuchung', {
          datum: form.datum.value, stundeVon: Number(form.stundeVon.value), stundeBis: Number(form.stundeBis.value), raum,
          klassen: form.klassen.value.split(/[,;\s]+/).filter(Boolean), titel: form.titel.value, text: form.text.value,
        });
        toast(`Raum ${raum} gebucht.`);
        kontext.navigieren('#/melden/meine');
      } catch (err) { kontext.fehler(err); }
    };
  }

  if (bereich === 'klausur') {
    ziel.innerHTML = `<div class="karte" style="max-width:860px"><div class="karte-kopf">${icon('klausur')}<h2>Klausur eintragen</h2></div><div class="karte-inhalt">
      <form class="formular">
        <div class="zeile"><label class="feld">Datum<input type="date" name="datum" value="${plusTage(heuteIso(), 7)}" min="${heuteIso()}" required></label>
          <label class="feld">von Stunde${stundenAuswahl(stunden, 'stundeVon', 3)}</label>
          <label class="feld">bis Stunde${stundenAuswahl(stunden, 'stundeBis', 4)}</label></div>
        <label class="feld">Titel<input name="titel" placeholder="z. B. Klausur Deutsch GK" required></label>
        <div class="zeile">
          <div><b class="klein">Klasse(n) / Kurse</b>${mehrfachAuswahl('klassen', el.klassen)}</div>
          <div><b class="klein">Räume (optional)</b>${mehrfachAuswahl('raeume', el.raeume)}</div>
        </div>
        <div><b class="klein">Aufsicht (optional)</b>${mehrfachAuswahl('aufsicht', el.lehrer)}</div>
        <label class="feld">Bemerkung (optional)<input name="text"></label>
        <div><button class="knopf haupt" type="submit">${icon('haken', 'icon-klein')} Klausur eintragen</button></div>
      </form></div></div>`;
    const form = ziel.querySelector('form');
    form.stundeVon.onchange = () => { if (Number(form.stundeBis.value) < Number(form.stundeVon.value)) form.stundeBis.value = form.stundeVon.value; };
    form.onsubmit = async ev => {
      ev.preventDefault();
      const klassen = gewaehlt(ziel, 'klassen');
      if (!klassen.length) { toast('Bitte mindestens eine Klasse auswählen.', true); return; }
      try {
        await senden('/api/app/meldungen/klausur', {
          datum: form.datum.value, stundeVon: Number(form.stundeVon.value), stundeBis: Number(form.stundeBis.value),
          klassen, raeume: gewaehlt(ziel, 'raeume'), aufsicht: gewaehlt(ziel, 'aufsicht'), titel: form.titel.value, text: form.text.value,
        });
        toast('Klausur eingetragen.');
        kontext.navigieren('#/melden/meine');
      } catch (err) { kontext.fehler(err); }
    };
  }

  if (bereich === 'meine') {
    const liste = await holen('/api/app/meldungen');
    ziel.innerHTML = `<div class="karte"><div class="karte-kopf">${icon('melden')}<h2>Meine Meldungen</h2></div>
      ${liste.length ? `<div class="tabelle-huelle"><table class="tabelle"><thead><tr><th>Meldung</th><th>Erstellt</th><th>Stand</th><th></th></tr></thead><tbody>
        ${liste.map(m => `<tr><td>${e(meldungText(m))}${m.text && m.typ !== 'nachricht' ? `<br><small class="gedimmt">${e(m.text)}</small>` : ''}</td>
          <td class="gedimmt" style="white-space:nowrap">${e(new Date(m.erstellt.replace(' ', 'T')).toLocaleString('de-DE', { dateStyle: 'short', timeStyle: 'short' }))}</td>
          <td>${statusMarker(m.status)}</td>
          <td style="text-align:right">${m.status === 'offen' ? `<button class="knopf klein flach gefahr" data-zurueck="${e(m.id)}">${icon('x', 'icon-klein')} Zurückziehen</button>` : ''}</td></tr>`).join('')}
      </tbody></table></div>` : '<div class="leer">Noch keine Meldungen.</div>'}</div>`;
    ziel.querySelectorAll('[data-zurueck]').forEach(b => b.onclick = async () => {
      if (!await bestaetigen('Meldung zurückziehen', 'Soll die Meldung zurückgezogen werden?', 'Zurückziehen')) return;
      try { await api(`/api/app/meldungen/${encodeURIComponent(b.dataset.zurueck)}`, { methode: 'DELETE' }); toast('Meldung zurückgezogen.'); kontext.navigieren(location.hash); }
      catch (err) { kontext.fehler(err); }
    });
  }
}
