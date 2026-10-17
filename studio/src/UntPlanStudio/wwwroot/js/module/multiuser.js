// Modul „MultiUser“: mehrere Benutzer arbeiten gleichzeitig an derselben Schuldatei. Anzeige, wer gerade aktiv ist (mit
// Tätigkeit = aktives Fenster), Bereichssperren und Exklusivbetrieb mit Sperrhinweisen, Nachrichten an andere Benutzer, eigene
// Rechte und – für Administratoren – Gruppen mit Rechten/Rechtestufen (UntPlan.Core.MultiUser) und Zuordnung der Benutzer.
import { h } from '../kern/dom.js';
import { symbol } from '../kern/symbole.js';
import { api } from '../kern/api.js';
import { zustand } from '../kern/zustand.js';
import { an } from '../kern/ereignisse.js';
import { dialog, formular, frage, hinweis, fehlerMelden } from '../kern/dialog.js';
import { fensterverwaltung } from '../kern/fenster.js';

let stand = null;
let nachrichtNach = 0;
let takt = 0;
let feld = null;
const beobachter = new Set();

function taetigkeit() {
  const f = fensterverwaltung.aktiv?.();
  return f?.titelText?.textContent || '';
}

function feldZeigen() {
  if (!feld) {
    const zeile = document.querySelector('.statuszeile');
    if (!zeile) return;
    feld = h('button', { class: 'feld mu-feld', type: 'button', title: 'Wer arbeitet gerade an dieser Datei? (MultiUser)', onclick: () => oeffnen() });
    zeile.append(feld);
  }
  if (!zustand.dokument || !stand) { feld.hidden = true; return; }
  feld.hidden = false;
  const andere = stand.aktive.filter((s) => s.aktiv && s.benutzer.toLowerCase() !== stand.ich.toLowerCase());
  const sperre = stand.exklusiv && !stand.exklusiv.eigen ? `exklusiv: ${stand.exklusiv.anzeigename}` : stand.sperren.length ? `${stand.sperren.length} Sperre(n)` : '';
  feld.replaceChildren(symbol('benutzer'), h('span', null, andere.length ? `${andere.length + 1} aktiv` : 'allein'), sperre ? h('span', { class: 'mu-sperre' }, sperre) : null);
  feld.title = stand.aktive.filter((s) => s.aktiv).map((s) => `${s.anzeigename}${s.taetigkeit ? ` – ${s.taetigkeit}` : ''}`).join('\n') || 'MultiUser';
}

async function abfragen() {
  if (!zustand.dokument) return;
  try {
    const neu = await api.get(api.dok(`/multiuser?nach=${nachrichtNach}&taetigkeit=${encodeURIComponent(taetigkeit())}`));
    const vorher = stand;
    stand = neu;
    for (const n of neu.nachrichten) {
      nachrichtNach = Math.max(nachrichtNach, n.id);
      if (n.von.toLowerCase() !== neu.ich.toLowerCase() && vorher) hinweis(`Nachricht von ${n.von}: ${n.text}`, 'info', 10000);
    }
    if (vorher) {
      const alt = new Set(vorher.aktive.filter((s) => s.aktiv).map((s) => s.benutzer));
      for (const s of neu.aktive) if (s.aktiv && !alt.has(s.benutzer) && s.benutzer !== neu.ich) hinweis(`${s.anzeigename} arbeitet jetzt ebenfalls an dieser Datei.`, 'info', 5000);
      if (neu.exklusiv && !neu.exklusiv.eigen && !vorher.exklusiv) hinweis(`${neu.exklusiv.anzeigename} arbeitet jetzt exklusiv – Änderungen sind gesperrt.`, 'warnung', 8000);
    }
    feldZeigen();
    for (const f of beobachter) f(stand);
  } catch { /* still: nächster Versuch */ }
}

function starten() {
  clearInterval(takt);
  stand = null;
  nachrichtNach = 0;
  feldZeigen();
  if (!zustand.dokument) return;
  abfragen();
  takt = setInterval(abfragen, 20000);
}

// ---------------------------------------------------------------------------------------------- Fenster

function multiuserFenster(f) {
  f.titel('MultiUser – Benutzer an dieser Datei');
  const flaeche = h('div', { class: 'mu-fenster' });
  f.inhalt.append(flaeche);
  f.werkzeug({ text: 'Neuanzeige', symbol: 'wiederholen', aktion: abfragen });
  f.werkzeug({ text: 'Nachricht', symbol: 'info', aktion: () => nachricht() });
  if (zustand.benutzer?.rolle === 'Admin') f.werkzeug({ text: 'Gruppen und Rechte', symbol: 'einstellungen', aktion: () => fensterverwaltung.oeffnen('multiuser.verwaltung', {}) });

  async function nachricht(an = '') {
    const andere = (stand?.aktive || []).filter((s) => s.benutzer !== stand.ich);
    const w = await formular('Nachricht senden', [
      { name: 'an', titel: 'An', art: 'auswahl', wert: an, optionen: [{ wert: '', text: 'alle Benutzer' }, ...andere.map((s) => ({ wert: s.benutzer, text: s.anzeigename }))] },
      { name: 'text', titel: 'Text', art: 'textfeld', pflicht: true }]);
    if (!w) return;
    await api.post(api.dok('/multiuser/nachricht'), w).catch(fehlerMelden);
    abfragen();
  }
  async function sperren(bereich, ein) {
    try {
      stand = ein ? await api.post(api.dok('/multiuser/sperren'), { bereich }) : await api.loeschen(api.dok(`/multiuser/sperren/${bereich}`));
      feldZeigen();
      zeichnen(stand);
    } catch (e) { fehlerMelden(e); }
  }
  async function exklusiv(ein) {
    try { stand = await api.post(api.dok('/multiuser/exklusiv'), { ein }); feldZeigen(); zeichnen(stand); hinweis(ein ? 'Exklusivbetrieb eingeschaltet.' : 'Exklusivbetrieb beendet.', 'ok'); } catch (e) { fehlerMelden(e); }
  }

  function zeichnen(s) {
    if (!s) { flaeche.replaceChildren(h('p', { class: 'leise' }, 'Wird geladen …')); return; }
    const admin = zustand.benutzer?.rolle === 'Admin';
    const sperreVon = (b) => s.sperren.find((x) => x.bereich === b);
    flaeche.replaceChildren(
      h('section', null, h('h3', null, 'Aktive Benutzer'),
        h('table', { class: 'mu-tabelle' }, h('thead', null, h('tr', null, ['Benutzer', 'Tätigkeit', 'seit', 'letzte Aktivität', ''].map((x) => h('th', null, x)))),
          h('tbody', null, s.aktive.map((a) => h('tr', { class: a.aktiv ? '' : 'mu-inaktiv' },
            h('td', null, h('span', { class: `mu-punkt${a.aktiv ? ' an' : ''}` }), a.anzeigename, a.benutzer === s.ich ? h('span', { class: 'leise' }, ' (Sie)') : null),
            h('td', null, a.taetigkeit), h('td', null, new Date(a.seit).toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })),
            h('td', null, new Date(a.aktivitaet).toLocaleTimeString('de-DE')),
            h('td', null, a.benutzer !== s.ich ? h('button', { class: 'knopf klein', type: 'button', onclick: () => nachricht(a.benutzer) }, 'Nachricht …') : null)))))),
      h('section', null, h('h3', null, 'Exklusivbetrieb'),
        s.exklusiv ? h('p', { class: s.exklusiv.eigen ? '' : 'mu-warnung' }, `${s.exklusiv.anzeigename} arbeitet exklusiv seit ${new Date(s.exklusiv.seit).toLocaleTimeString('de-DE')}.`) : h('p', { class: 'leise' }, 'Aus – alle berechtigten Benutzer können gleichzeitig ändern (Änderungen erscheinen sofort bei allen).'),
        h('button', { class: 'knopf', type: 'button', disabled: !zustand.benutzer?.darfSchreiben || (s.exklusiv && !s.exklusiv.eigen && !admin), onclick: () => exklusiv(!s.exklusiv) }, s.exklusiv ? 'Exklusivbetrieb beenden' : 'Exklusivbetrieb einschalten')),
      h('section', null, h('h3', null, `Bereiche – Ihre Rechte (Gruppe ${s.rechte.gruppe}${s.rechte.nurLesen ? ', nur lesen' : ''})`),
        h('table', { class: 'mu-tabelle' }, h('thead', null, h('tr', null, ['Bereich', 'Ihre Stufe', 'Sperre', ''].map((x) => h('th', null, x)))),
          h('tbody', null, s.rechte.bereiche.map((b) => {
            const sp = sperreVon(b.bereich);
            const eigen = sp && sp.benutzer.toLowerCase() === s.ich.toLowerCase();
            return h('tr', { class: sp && !eigen ? 'mu-gesperrt' : '' }, h('td', null, b.text), h('td', null, b.stufeText),
              h('td', null, sp ? `${eigen ? 'von Ihnen' : sp.anzeigename} seit ${new Date(sp.seit).toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })}` : ''),
              h('td', null, sp ? h('button', { class: 'knopf klein', type: 'button', disabled: !eigen && !admin, onclick: () => sperren(b.bereich, false) }, 'Freigeben')
                : h('button', { class: 'knopf klein', type: 'button', disabled: !b.bearbeiten || !zustand.benutzer?.darfSchreiben, title: 'Bereich zur alleinigen Bearbeitung sperren', onclick: () => sperren(b.bereich, true) }, 'Sperren')));
          }))),
        h('p', { class: 'leise' }, 'Sperren verfallen automatisch, wenn ihr Inhaber einige Minuten nicht mehr aktiv ist.')),
      h('section', null, h('h3', null, 'Nachrichten'),
        s.nachrichten.length || verlauf.length ? h('div', { class: 'mu-nachrichten' }, [...verlauf].reverse().map((n) => h('div', null, h('b', null, `${n.von}${n.an ? ` → ${n.an}` : ''} `), h('span', { class: 'leise' }, new Date(n.zeit).toLocaleTimeString('de-DE')), ' ', n.text))) : h('p', { class: 'leise' }, 'Keine Nachrichten.')));
  }
  const verlauf = [];
  const aufnehmen = (s) => { for (const n of s.nachrichten) if (!verlauf.some((x) => x.id === n.id)) verlauf.push(n); zeichnen(s); };
  beobachter.add(aufnehmen);
  f.beimSchliessen(() => beobachter.delete(aufnehmen));
  // vollständige Nachrichtenliste einmal holen
  api.get(api.dok('/multiuser?nach=0')).then((s) => { for (const n of s.nachrichten) verlauf.push(n); stand = stand || s; zeichnen(s); }).catch(fehlerMelden);
  zeichnen(stand);
}

function verwaltungFenster(f) {
  f.titel('MultiUser – Gruppen und Rechte');
  const flaeche = h('div', { class: 'mu-fenster' });
  f.inhalt.append(flaeche);
  let v = null;
  f.werkzeug({ text: 'Neue Gruppe', symbol: 'plus', aktion: async () => {
    const w = await formular('Neue Gruppe', [{ name: 'name', titel: 'Gruppenname', pflicht: true }]);
    if (w) { v = await api.post('/api/multiuser/gruppen', { name: w.name, rechte: [], stufen: {} }).catch(fehlerMelden) || v; zeichnen(); }
  } });

  async function gruppeBearbeiten(g) {
    const rechte = v.rechteListe.map((r) => ({ r, c: h('input', { type: 'checkbox', checked: g.rechte.includes(r.recht) }) }));
    const stufen = v.bereiche.map((b) => ({ b, s: h('select', { class: 'eingabe' }, v.stufen.map((s) => h('option', { value: s.stufe, selected: g.stufen[b.bereich] === s.stufe }, s.text))) }));
    const name = h('input', { class: 'eingabe', value: g.name });
    const ok = await dialog({ titel: `Gruppe ${g.name}`, breite: '720px', inhalt: h('div', { class: 'mu-gruppe' },
      h('div', { class: 'formular' }, h('label', null, 'Name'), name),
      h('div', { class: 'mu-spalten' },
        h('fieldset', null, h('legend', null, 'Rechte'), ...rechte.map(({ r, c }) => h('label', { class: 'kontrolle' }, c, r.text))),
        h('fieldset', null, h('legend', null, 'Rechtestufen je Bereich'), h('div', { class: 'formular' }, ...stufen.flatMap(({ b, s }) => [h('label', null, b.text), s]))))),
    knoepfe: [{ text: 'Speichern', wert: true, primaer: true }, { text: 'Abbrechen', wert: null }] });
    if (!ok) return;
    try {
      v = await api.put(`/api/multiuser/gruppen/${g.id}`, { name: name.value, rechte: rechte.filter((x) => x.c.checked).map((x) => x.r.recht), stufen: Object.fromEntries(stufen.map(({ b, s }) => [b.bereich, s.value])) });
      zeichnen();
    } catch (e) { fehlerMelden(e); }
  }

  function zeichnen() {
    if (!v) return;
    flaeche.replaceChildren(
      h('section', null, h('h3', null, 'Gruppen'),
        h('table', { class: 'mu-tabelle' }, h('thead', null, h('tr', null, h('th', null, 'Gruppe'), ...v.bereiche.map((b) => h('th', null, b.text)), h('th', null, 'Rechte'), h('th'))),
          h('tbody', null, v.gruppen.map((g) => h('tr', null, h('td', null, g.name), ...v.bereiche.map((b) => h('td', null, v.stufen.find((s) => s.stufe === g.stufen[b.bereich])?.text || '')),
            h('td', { class: 'leise' }, g.rechte.map((r) => v.rechteListe.find((x) => x.recht === r)?.text || r).join(', ')),
            h('td', null, h('button', { class: 'knopf klein', type: 'button', onclick: () => gruppeBearbeiten(g) }, 'Bearbeiten …'),
              h('button', { class: 'knopf klein gefahr', type: 'button', onclick: async () => { if (await frage(`Gruppe „${g.name}“ löschen?`, { gefahr: true, ja: 'Löschen' })) { v = await api.loeschen(`/api/multiuser/gruppen/${g.id}`).catch(fehlerMelden) || v; zeichnen(); } } }, symbol('loeschen')))))))),
      h('section', null, h('h3', null, 'Benutzer'),
        h('p', { class: 'leise' }, 'Rolle Admin hat immer alle Rechte, Rolle Leser liest nur. Planer ohne Gruppe dürfen alle Bereiche bearbeiten.'),
        h('table', { class: 'mu-tabelle' }, h('thead', null, h('tr', null, ['Benutzer', 'Rolle', 'Gruppe', 'nur lesen', 'Abteilung', 'wirksam'].map((x) => h('th', null, x)))),
          h('tbody', null, v.benutzer.map((b) => {
            const gruppe = h('select', { class: 'eingabe', disabled: b.rolle !== 'Planer' }, h('option', { value: 0 }, '– keine –'), ...v.gruppen.map((g) => h('option', { value: g.id, selected: b.zuordnung?.gruppeId === g.id }, g.name)));
            const nurLesen = h('input', { type: 'checkbox', checked: !!b.zuordnung?.nurLesen, disabled: b.rolle !== 'Planer' });
            const abteilung = h('input', { class: 'eingabe', value: b.zuordnung?.abteilung || '', style: { width: '90px' }, disabled: b.rolle !== 'Planer' });
            const speichern = async () => {
              try { v = await api.put(`/api/multiuser/benutzer/${encodeURIComponent(b.name)}`, { gruppeId: Number(gruppe.value), nurLesen: nurLesen.checked, abteilung: abteilung.value }); zeichnen(); hinweis('Zuordnung gespeichert.', 'ok', 2000); } catch (e) { fehlerMelden(e); }
            };
            for (const el of [gruppe, nurLesen, abteilung]) el.addEventListener('change', speichern);
            return h('tr', null, h('td', null, `${b.anzeigename} (${b.name})`), h('td', null, b.rolle), h('td', null, gruppe), h('td', null, nurLesen), h('td', null, abteilung), h('td', { class: 'leise' }, b.wirksam));
          })))));
  }
  api.get('/api/multiuser/verwaltung').then((x) => { v = x; zeichnen(); }).catch(fehlerMelden);
}

function oeffnen() {
  if (zustand.dokument) fensterverwaltung.oeffnen('multiuser', {});
  else if (zustand.benutzer?.rolle === 'Admin') fensterverwaltung.oeffnen('multiuser.verwaltung', {});
}

export default {
  id: 'multiuser',
  css: ['css/module/multiuser.css'],
  fenster: {
    multiuser: { titel: 'MultiUser', symbol: 'datenbank', breite: 820, hoehe: 600, einzig: () => 'mu', erstellen: multiuserFenster },
    'multiuser.verwaltung': { titel: 'Gruppen und Rechte', symbol: 'einstellungen', breite: 980, hoehe: 560, einzig: () => 'muv', erstellen: verwaltungFenster },
  },
  befehle: { 'multiuser.oeffnen': oeffnen, 'multiuser.verwaltung': () => fensterverwaltung.oeffnen('multiuser.verwaltung', {}) },
  ribbon: [
    {
      id: 'start.multiuser', reiter: 'Start', gruppe: 'Schule', text: 'MultiUser', symbol: 'datenbank', gross: true, hinweis: 'Mehrere Benutzer an einer Datei: aktive Benutzer, Sperren, Exklusivbetrieb, Rechte',
      menue: [
        { text: 'Angemeldete Benutzer …', symbol: 'benutzer', befehl: 'multiuser.oeffnen', braucht: 'dokument' },
        { text: 'Neuanzeige', symbol: 'wiederholen', aktion: abfragen, braucht: 'dokument' },
        { text: 'Gruppen und Rechte …', symbol: 'einstellungen', befehl: 'multiuser.verwaltung', braucht: 'admin' },
      ],
    },
  ],
  start() {
    an('dokument', starten);
    // Änderungen anderer Benutzer in der Statuszeile nennen
    an('daten', (e) => { if (e?.benutzer && stand && e.benutzer !== stand.ich && e.quelle == null) feldZeigen(); });
    document.addEventListener('visibilitychange', () => { if (!document.hidden) abfragen(); });
    starten();
  },
};
