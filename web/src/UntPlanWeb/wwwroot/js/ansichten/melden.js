// Melden im Portal-Design: Übersicht der Meldearten als Kacheln (Abwesenheit melden, Prüfung eintragen, Raum buchen, Meine Meldungen)
// mit den letzten Meldungen, einheitliche Formulare (Karte mit Kopf, Feldraster, Fuß mit Abbrechen/Absenden, Auswahlen über die
// gemeinsame Auswahl-Komponente, Raumwahl mit Belegung) und „Meine Meldungen“ mit Suche, Status- und Art-Filter, Status-Pills und
// leeren Zuständen. Raum buchen führt in das Buchungsmodul.
import { e, holen, senden, api, toast, bestaetigen, heuteIso, plusTage, meldungText, datumLang, suchfeldMitFilter, suchfeldMitFilterBinden } from '../ui.js';
import { raumwahl, raumTokens } from '../raumwahl.js';

const BEREICHE = ['uebersicht', 'abwesenheit', 'klausur', 'meine'];
const TITEL = { uebersicht: 'Melden', abwesenheit: 'Abwesenheit melden', klausur: 'Prüfung eintragen', meine: 'Meine Meldungen' };
const GRUENDE = ['Krankheit', 'Fortbildung', 'Dienstlicher Termin', 'Klassenfahrt / Exkursion', 'Sonderurlaub', 'Sonstiges'];
const STATUS = { offen: { text: 'offen', klasse: 'offen' }, quittiert: { text: 'übernommen', klasse: 'ok' }, abgelehnt: { text: 'abgelehnt', klasse: 'nein' } };
const ART = {
  absenz: { text: 'Abwesenheit', symbol: 'person-dash' }, klausur: { text: 'Prüfung', symbol: 'journal-check' }, klausurloeschen: { text: 'Prüfung löschen', symbol: 'journal-x' },
  raumbuchung: { text: 'Raumbuchung', symbol: 'door-open' }, buchungstorno: { text: 'Storno', symbol: 'x-circle' }, nachricht: { text: 'Nachricht', symbol: 'chat-left-text' },
};
const artVon = m => ART[m.typ] ?? ART.nachricht;
const titelVon = m => meldungText(m).replace(/^Klausur /, 'Prüfung ');

function stileLaden() {
  if (document.querySelector('link[data-melden-css]')) return;
  const l = document.createElement('link');
  l.rel = 'stylesheet';
  l.href = 'css/melden.css';
  l.dataset.meldenCss = '1';
  document.head.append(l);
}

const feld = (beschriftung, html, { klasse = '', optional = false, fuer = '' } = {}) =>
  `<div class="mf-feld ${klasse}"><label ${fuer ? `for="${fuer}"` : ''}>${e(beschriftung)}${optional ? ' <span class="mf-optional">optional</span>' : ''}</label>${html}</div>`;
const stundenAuswahl = (stunden, name, wert) => `<select class="form-select" name="${name}">${stunden.map(s => `<option value="${s.nr}" ${s.nr === wert ? 'selected' : ''} ${s.beginn ? `data-zweit="${e(s.beginn)}${s.ende ? `–${e(s.ende)}` : ''}"` : ''}>${e(s.bezeichnung || s.nr)}. Stunde</option>`).join('')}</select>`;
const mehrfach = (name, eintraege, platzhalter) => `<select class="form-select" name="${name}" multiple data-platzhalter="${e(platzhalter)}">${eintraege.map(x => `<option value="${e(x.kurz)}" ${x.name && x.name !== x.kurz ? `data-zweit="${e(x.name)}"` : ''}>${e(x.kurz)}</option>`).join('')}</select>`;
const karte = (symbol, titel, unter, inhalt) => `<form class="mf-karte" novalidate>
    <header class="mf-kopf"><span class="mf-symbol"><i class="bi bi-${symbol}" aria-hidden="true"></i></span><div><h2>${e(titel)}</h2><p>${e(unter)}</p></div></header>
    <div class="mf-rumpf">${inhalt}</div></form>`;
const fussLeiste = text => `<footer class="mf-fuss"><a class="nl-knopf" href="#/melden">Abbrechen</a><button class="nl-hinzu" type="submit"><i class="bi bi-send" aria-hidden="true"></i>${e(text)}</button></footer>`;
const statusPill = s => { const x = STATUS[s] ?? { text: s, klasse: '' }; return `<span class="mp-status ${x.klasse}">${e(x.text)}</span>`; };
const erstelltText = m => { const d = new Date(String(m.erstellt ?? '').replace(' ', 'T')); return isNaN(d) ? '' : d.toLocaleString('de-DE', { dateStyle: 'short', timeStyle: 'short' }); };

/** Absenden mit gesperrtem Knopf (kein Doppelklick) und Fehlermeldung. */
async function absenden(form, kontext, aktion) {
  const knopf = form.querySelector('[type=submit]');
  knopf.disabled = true;
  try { await aktion(); } catch (err) { kontext.fehler(err); } finally { knopf.disabled = false; }
}

export async function zeigen(seite, kontext, [bereichParam]) {
  stileLaden();
  if (bereichParam === 'raum') { kontext.navigieren('#/buchen/raum', true); return; }
  const bereich = BEREICHE.includes(bereichParam) ? bereichParam : 'uebersicht';
  kontext.titel(TITEL[bereich]);
  const el = await kontext.elemente();
  const stunden = (el.stunden ?? []).map(s => ({ nr: s.nr, bezeichnung: s.bezeichnung, beginn: s.beginn, ende: s.ende })).filter(s => s.nr > 0);
  if (!stunden.length) for (let i = 1; i <= 10; i++) stunden.push({ nr: i, bezeichnung: String(i) });
  const letzte = stunden[stunden.length - 1].nr;
  seite.innerHTML = `<div class="mf-seite mf-${bereich}" data-bereich></div>`;
  const ziel = seite.querySelector('[data-bereich]');

  // ---------------------------------------------------------------- Übersicht: Meldearten als Kacheln, letzte Meldungen

  if (bereich === 'uebersicht') {
    const liste = await holen('/api/app/meldungen').catch(() => []);
    const offen = liste.filter(m => m.status === 'offen').length;
    const kachel = (href, symbol, titel, text, zusatz = '') => `<a class="mf-kachel" href="${href}"><span class="mf-symbol"><i class="bi bi-${symbol}" aria-hidden="true"></i></span>
        <span class="mf-kachel-text"><b>${e(titel)}</b><span>${e(text)}</span></span>${zusatz}<i class="bi bi-chevron-right mf-pfeil" aria-hidden="true"></i></a>`;
    const letzteMeldungen = liste.slice(0, 4);
    ziel.innerHTML = `
      <p class="mf-einleitung">Was möchten Sie melden? Meldungen gehen an die Planung und erscheinen nach der Übernahme im Stundenplan.</p>
      <div class="mf-kacheln">
        ${kachel('#/melden/abwesenheit', 'person-dash', 'Abwesenheit melden', 'Krankheit, Fortbildung oder Termin – ganztägig oder für einzelne Stunden')}
        ${kachel('#/melden/klausur', 'journal-check', 'Prüfung eintragen', 'Klassenarbeit, Klausur oder Test mit Raum und Aufsicht')}
        ${kachel('#/buchen/raum', 'door-open', 'Raum buchen', 'Freien Raum für eine Stunde oder als Serie buchen')}
        ${kachel('#/melden/meine', 'list-check', 'Meine Meldungen', 'Status verfolgen, offene Meldungen zurückziehen', offen ? `<span class="mf-zaehler" title="offene Meldungen">${offen}</span>` : '')}
      </div>
      <section class="mf-block">
        <header><h2>Zuletzt gemeldet</h2>${liste.length ? '<a href="#/melden/meine">Alle anzeigen</a>' : ''}</header>
        ${letzteMeldungen.length ? `<ul class="mp-liste kompakt">${letzteMeldungen.map(m => meldungZeile(m, false)).join('')}</ul>`
          : `<div class="mf-leer"><i class="bi bi-inbox" aria-hidden="true"></i><b>Noch keine Meldungen</b><span>Ihre Meldungen erscheinen hier mit ihrem Status.</span></div>`}
      </section>`;
    return;
  }

  // ---------------------------------------------------------------- Abwesenheit melden

  if (bereich === 'abwesenheit') {
    ziel.innerHTML = karte('person-dash', 'Abwesenheit melden', 'Die Meldung geht an die Vertretungsplanung und wird dort als Abwesenheit übernommen.', `
      <div class="mf-raster">
        ${feld('Von', `<input type="date" class="form-control" name="von" value="${heuteIso()}" required>`)}
        ${feld('Bis', `<input type="date" class="form-control" name="bis" value="${heuteIso()}" required>`)}
        ${feld('Umfang', `<div class="mf-segment" role="radiogroup" aria-label="Umfang">
            <button type="button" role="radio" aria-checked="true" class="an" data-umfang="ganz">Ganztägig</button>
            <button type="button" role="radio" aria-checked="false" data-umfang="stunden">Einzelne Stunden</button></div>`, { klasse: 'breit' })}
        <div class="mf-unterraster breit d-none" data-stunden>
          ${feld('Ab', stundenAuswahl(stunden, 'stundeVon', 1))}${feld('Bis', stundenAuswahl(stunden, 'stundeBis', letzte))}</div>
        ${feld('Grund', `<div class="mf-chips" role="radiogroup" aria-label="Grund">${GRUENDE.map((g, i) => `<button type="button" role="radio" class="mf-chip ${i === 0 ? 'an' : ''}" aria-checked="${i === 0}" data-grund="${e(g)}">${e(g)}</button>`).join('')}</div>`, { klasse: 'breit' })}
        ${feld('Hinweise für die Vertretung', '<textarea class="form-control" name="text" rows="3" maxlength="1000" placeholder="z. B. Material liegt im Fach, Aufgaben im Kurs"></textarea>', { klasse: 'breit', optional: true })}
      </div>
      <div class="mf-zusammenfassung" data-zf></div>
      ${fussLeiste('Abwesenheit melden')}`);
    const form = ziel.querySelector('form');
    let ganz = true, grund = GRUENDE[0];
    const zf = () => {
      const zeitraum = form.von.value === form.bis.value || !form.bis.value ? datumLang(form.von.value) : `${datumLang(form.von.value)} bis ${datumLang(form.bis.value)}`;
      form.querySelector('[data-zf]').innerHTML = form.von.value ? `<i class="bi bi-info-circle" aria-hidden="true"></i><span>${e(zeitraum)}${ganz ? ', ganztägig' : `, ${form.stundeVon.value}.–${form.stundeBis.value}. Stunde`} · ${e(grund)}</span>` : '';
    };
    form.querySelectorAll('[data-umfang]').forEach(b => b.onclick = () => {
      ganz = b.dataset.umfang === 'ganz';
      form.querySelectorAll('[data-umfang]').forEach(x => { x.classList.toggle('an', x === b); x.setAttribute('aria-checked', String(x === b)); });
      form.querySelector('[data-stunden]').classList.toggle('d-none', ganz);
      zf();
    });
    form.querySelectorAll('[data-grund]').forEach(b => b.onclick = () => {
      grund = b.dataset.grund;
      form.querySelectorAll('[data-grund]').forEach(x => { x.classList.toggle('an', x === b); x.setAttribute('aria-checked', String(x === b)); });
      zf();
    });
    form.von.onchange = () => { if (form.bis.value < form.von.value) form.bis.value = form.von.value; zf(); };
    form.bis.onchange = form.stundeVon.onchange = form.stundeBis.onchange = () => {
      if (Number(form.stundeBis.value) < Number(form.stundeVon.value)) form.stundeBis.value = form.stundeVon.value;
      zf();
    };
    zf();
    form.onsubmit = ev => {
      ev.preventDefault();
      if (!form.von.value || !form.bis.value) { toast('Bitte den Zeitraum angeben.', true); return; }
      absenden(form, kontext, async () => {
        await senden('/api/app/meldungen/abwesenheit', {
          von: form.von.value, bis: form.bis.value, grund, text: form.text.value,
          stundeVon: ganz ? null : Number(form.stundeVon.value), stundeBis: ganz ? null : Number(form.stundeBis.value),
        });
        toast('Abwesenheit gemeldet.');
        kontext.navigieren('#/melden/meine');
      });
    };
  }

  // ---------------------------------------------------------------- Prüfung eintragen

  if (bereich === 'klausur') {
    ziel.innerHTML = karte('journal-check', 'Prüfung eintragen', 'Die Prüfung steht sofort im Stundenplan und wird von UntPlan übernommen.', `
      <div class="mf-raster">
        ${feld('Titel', '<input class="form-control" name="titel" maxlength="200" placeholder="z. B. Deutsch Klassenarbeit" required>', { klasse: 'breit' })}
        ${feld('Datum', `<input type="date" class="form-control" name="datum" value="${plusTage(heuteIso(), 7)}" min="${heuteIso()}" required>`)}
        <div class="mf-unterraster">${feld('Von', stundenAuswahl(stunden, 'stundeVon', 3))}${feld('Bis', stundenAuswahl(stunden, 'stundeBis', 4))}</div>
        ${feld('Klassen / Kurse', mehrfach('klassen', el.klassen, 'Klassen wählen'))}
        ${feld('Räume', '<input class="form-control" id="mk-raeume" name="raeume">', { optional: true, fuer: 'mk-raeume' })}
        ${feld('Aufsicht', mehrfach('aufsicht', el.lehrer, 'Lehrkräfte wählen'), { optional: true })}
        ${feld('Bemerkung', '<input class="form-control" name="text" maxlength="500">', { optional: true })}
      </div>
      ${fussLeiste('Prüfung eintragen')}`);
    const form = ziel.querySelector('form');
    form.stundeVon.addEventListener('change', () => { if (Number(form.stundeBis.value) < Number(form.stundeVon.value)) form.stundeBis.value = form.stundeVon.value; });
    // Raumwahl mit Suche und Belegung zur gewählten Zeit (Datum, Von, Bis)
    raumwahl(form.raeume, { wurzel: form, felder: { datum: 'datum', von: 'stundeVon', bis: 'stundeBis' } });
    const werte = name => [...form[name].selectedOptions].map(o => o.value);
    form.onsubmit = ev => {
      ev.preventDefault();
      if (!form.titel.value.trim()) { toast('Bitte einen Titel angeben.', true); form.titel.focus(); return; }
      const klassen = werte('klassen');
      if (!klassen.length) { toast('Bitte mindestens eine Klasse auswählen.', true); return; }
      absenden(form, kontext, async () => {
        await senden('/api/app/meldungen/klausur', {
          datum: form.datum.value, stundeVon: Number(form.stundeVon.value), stundeBis: Number(form.stundeBis.value),
          klassen, raeume: raumTokens(form.raeume.value), aufsicht: werte('aufsicht'), titel: form.titel.value.trim(), text: form.text.value,
        });
        toast('Prüfung eingetragen.');
        kontext.navigieren('#/melden/meine');
      });
    };
  }

  // ---------------------------------------------------------------- Meine Meldungen

  if (bereich === 'meine') {
    const liste = await holen('/api/app/meldungen');
    const filter = { suche: '', status: '', art: '' };
    const arten = [...new Set(liste.map(m => (ART[m.typ] ? m.typ : 'nachricht')))];
    kontext.titel('Meine Meldungen', '', String(liste.length));
    const anzahl = s => liste.filter(m => !s || m.status === s).length;
    ziel.innerHTML = `
      <div class="nl-leiste mp-leiste">
        ${suchfeldMitFilter({ platzhalter: 'Meldungen durchsuchen', label: 'Meldungen durchsuchen', filter: [
          { name: 'status', label: 'Status', alle: `Jeder Status (${anzahl('')})`, optionen: [['offen', 'Offen'], ['quittiert', 'Übernommen'], ['abgelehnt', 'Abgelehnt']].map(([w, t]) => [w, `${t} (${anzahl(w)})`]) },
          ...(arten.length > 1 ? [{ name: 'art', label: 'Art', alle: 'Alle Arten', optionen: arten.map(a => [a, ART[a].text]) }] : []),
        ] })}
        <span class="ms-auto d-flex gap-2"><a class="nl-hinzu" href="#/melden"><i class="bi bi-plus-lg" aria-hidden="true"></i>Neue Meldung</a></span>
      </div>
      <div data-liste></div>`;
    const sichtbar = () => liste.filter(m => (!filter.status || m.status === filter.status) && (!filter.art || (ART[m.typ] ? m.typ : 'nachricht') === filter.art)
      && (!filter.suche || `${titelVon(m)} ${m.text ?? ''} ${artVon(m).text}`.toLowerCase().includes(filter.suche)));
    const zeichnen = () => {
      const l = sichtbar();
      const box = ziel.querySelector('[data-liste]');
      box.innerHTML = !liste.length
        ? `<div class="mf-leer gross"><i class="bi bi-inbox" aria-hidden="true"></i><b>Noch keine Meldungen</b><span>Melden Sie eine Abwesenheit oder tragen Sie eine Prüfung ein – hier sehen Sie dann den Status.</span><a class="nl-hinzu" href="#/melden"><i class="bi bi-plus-lg" aria-hidden="true"></i>Neue Meldung</a></div>`
        : l.length ? `<ul class="mp-liste">${l.map(m => meldungZeile(m, true)).join('')}</ul>`
          : `<div class="mf-leer gross"><i class="bi bi-funnel" aria-hidden="true"></i><b>Keine Meldungen für diesen Filter</b><button type="button" class="nl-knopf" data-reset>Filter zurücksetzen</button></div>`;
      box.querySelector('[data-reset]')?.addEventListener('click', () => {
        Object.assign(filter, { suche: '', status: '', art: '' });
        ziel.querySelector('[data-suche]').value = '';
        sf.setzen('status', ''); sf.setzen('art', '');
        zeichnen();
      });
      // Prüfung löschen – vor der Übernahme zurückziehen, danach Löschmeldung an UntPlan
      box.querySelectorAll('[data-klausur-loeschen]').forEach(b => b.onclick = async () => {
        const m = liste.find(x => x.id === b.dataset.klausurLoeschen);
        const { pruefungLoeschen } = await import('./pruefungen.js');
        if (await pruefungLoeschen({ datum: m.von, stundeVon: m.stundeVon ?? 1, stundeBis: m.stundeBis ?? m.stundeVon ?? 1, klassen: m.klassen ?? [], titel: m.titel })) kontext.navigieren(location.hash);
      });
      box.querySelectorAll('[data-zurueck]').forEach(b => b.onclick = async () => {
        if (!await bestaetigen('Meldung zurückziehen', 'Soll die Meldung zurückgezogen werden?', 'Zurückziehen')) return;
        try { await api(`/api/app/meldungen/${encodeURIComponent(b.dataset.zurueck)}`, { methode: 'DELETE' }); toast('Meldung zurückgezogen.'); kontext.navigieren(location.hash); }
        catch (err) { kontext.fehler(err); }
      });
    };
    // gemeinsames Suchfeld mit Filter: Status und Art als Dropdowns im Suchfeld (gewählt = Chip mit x)
    const sf = suchfeldMitFilterBinden(ziel, {
      beiSuche: t => { filter.suche = t.toLowerCase(); zeichnen(); },
      beiFilter: (name, wert) => { filter[name] = wert; zeichnen(); },
    });
    zeichnen();
  }
}

/** Zeile einer Meldung: Symbol der Art, Text, Zusatz, erstellt, Status-Pill, Aktionen (Prüfung löschen bzw. zurückziehen). */
function meldungZeile(m, mitAktionen) {
  const a = artVon(m);
  const aktion = !mitAktionen ? '' : m.typ === 'klausur' && (m.von ?? '') >= heuteIso()
    ? `<button type="button" class="nl-knopf rot klein" data-klausur-loeschen="${e(m.id)}"><i class="bi bi-trash" aria-hidden="true"></i>Löschen</button>`
    : m.status === 'offen' ? `<button type="button" class="nl-knopf klein" data-zurueck="${e(m.id)}"><i class="bi bi-arrow-counterclockwise" aria-hidden="true"></i>Zurückziehen</button>` : '';
  return `<li class="mp-zeile ${e(m.status)}">
      <span class="mp-symbol ${e(m.typ)}" title="${e(a.text)}"><i class="bi bi-${a.symbol}" aria-hidden="true"></i></span>
      <div class="mp-text"><div class="mp-titel">${e(titelVon(m))}</div>
        <div class="mp-unter"><span>${e(a.text)}</span>${erstelltText(m) ? `<span>gemeldet ${e(erstelltText(m))}</span>` : ''}${m.text && m.typ !== 'nachricht' ? `<span class="mp-notiz">${e(m.text)}</span>` : ''}</div></div>
      ${statusPill(m.status)}
      ${mitAktionen ? `<div class="mp-aktion">${aktion}</div>` : ''}</li>`;
}
