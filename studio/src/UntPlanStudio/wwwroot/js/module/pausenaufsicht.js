// Modul „Pausenaufsichten“ (Desktop: PausenaufsichtFenster): Reiter Gang (Aufsichtsbereich) bzw. Lehrer mit der Matrix
// Tage × Aufsichtspausen, Lehrerliste (PA Max/Ist/Anzahl), Lehrervorschlag mit Bewertung, Aufsicht eintragen/löschen/teilen,
// „keine Aufsicht“, Optimierung und Gewichtung; dazu die Vertretung der Aufsichten eines Tages (Vertretungsmodus).
import { h } from '../kern/dom.js';
import { api } from '../kern/api.js';
import { zustand, darfSchreiben } from '../kern/zustand.js';
import { betrifft } from '../kern/ereignisse.js';
import { Tabelle } from '../kern/tabelle.js';
import { dialog, frage, hinweis, laden, fehlerMelden, menue } from '../kern/dialog.js';
import { fensterverwaltung } from '../kern/fenster.js';

function planFenster(f, p) {
  f.titel('Pausenaufsichten');
  const schreiben = darfSchreiben();
  let sicht = p.sicht || 'gang';
  let liste = { gaenge: [], lehrer: [] };
  let daten = null;
  let akt = null; // { tag, nach }
  const reiter = h('div', { class: 'pa-reiter' });
  const element = h('select', { class: 'eingabe', style: { width: '120px' } });
  const minuten = h('span', { class: 'leise' });
  const raster = h('div', { class: 'pa-raster', tabindex: '0' });
  const seite = h('div', { class: 'pa-lehrerliste' });
  f.inhalt.append(reiter, h('div', { class: 'pa-teilung' }, raster, seite));

  f.werkzeuge.append(element);
  f.werkzeug({ text: '◀', titel: 'Vorheriges Element', nurSymbol: false, aktion: () => blaettern(-1) });
  f.werkzeug({ text: '▶', titel: 'Nächstes Element', aktion: () => blaettern(1) });
  f.werkzeuge.append(minuten);
  f.trenner();
  if (schreiben) {
    f.werkzeug({ text: 'Vorschlag', symbol: 'lehrer', titel: 'Lehrervorschlag mit Bewertung', aktion: () => vorschlag() });
    f.werkzeug({ text: 'Entfernen', symbol: 'loeschen', titel: 'Aufsicht löschen', aktion: () => zelleLoeschen() });
    f.werkzeug({ text: 'Keine Aufsicht', symbol: 'abwesenheit', titel: 'Aufsicht (???) bzw. keine Aufsicht', aktion: () => teile(0) });
    f.werkzeug({ text: 'Teilen', symbol: 'plus', titel: 'Aufsicht teilen', aktion: () => teile(1) });
    f.werkzeug({ text: 'Teilung –', symbol: 'zeile_loeschen', titel: 'Teilung entfernen', aktion: () => teile(-1) });
    f.trenner();
    f.werkzeug({ text: 'Optimierung', symbol: 'optimierung', aktion: optimieren });
    f.werkzeug({ text: 'Gewichtung', symbol: 'statistik', aktion: gewichtung });
    f.werkzeug({ text: 'Alle …', symbol: 'loeschen', titel: 'Aufsichten des Elements bzw. alle löschen', aktion: () => {
      const r = f.werkzeuge.getBoundingClientRect();
      menue(r.right - 260, r.bottom, [
        { text: 'Aufsichten dieses Elements löschen', aktion: async () => { if (await frage('Alle Aufsichten dieses Elements löschen?', { gefahr: true, ja: 'Löschen' })) { await api.post(api.dok('/pausenaufsicht/loeschen'), { sicht, id: Number(element.value) }); neu(); } } },
        { text: 'Alle Aufsichten löschen', aktion: async () => { if (await frage('Alle Pausenaufsichten löschen?', { gefahr: true, ja: 'Löschen' })) { await api.post(api.dok('/pausenaufsicht/loeschen'), { sicht: 'alle', id: 0 }); neu(); } } },
      ]);
    } });
  }
  f.werkzeug({ text: 'Gänge', symbol: 'gaenge', titel: 'Aufsichtsbereiche (Gänge) bearbeiten', aktion: () => fensterverwaltung.oeffnen('stammdaten', { typ: 'gang' }) });

  function reiterZeichnen() {
    reiter.replaceChildren(...[['gang', 'Gang'], ['lehrer', 'Lehrer']].map(([id, t]) => h('button', { type: 'button', class: id === sicht ? 'aktiv' : '', onclick: () => { sicht = id; f.parameterSetzen({ sicht }); elementeFuellen(); neu(); } }, t)));
  }
  function elementeFuellen() {
    const alt = Number(element.value);
    const l = sicht === 'gang' ? liste.gaenge : liste.lehrer;
    element.replaceChildren(...l.map((x) => h('option', { value: x.id, selected: x.id === alt || x.id === p.element }, x.kurz)));
    reiterZeichnen();
  }
  function blaettern(d) {
    const n = element.options.length;
    if (!n) return;
    element.selectedIndex = (element.selectedIndex + d + n) % n;
    neu();
  }
  element.addEventListener('change', () => { f.parameterSetzen({ element: Number(element.value) }); neu(); });

  function zeichnen() {
    if (!daten) { raster.replaceChildren(h('div', { class: 'leise pa-leer' }, sicht === 'gang' ? 'Keine Gänge angelegt – bitte unter Dateneingabe → Gänge anlegen.' : 'Keine Lehrkräfte.')); return; }
    minuten.textContent = daten.minuten;
    const kopf = h('tr', null, h('th'), ...daten.spalten.map((s) => h('th', { title: `${s.minuten} Minuten` }, h('div', null, s.name), h('div', { class: 'leise' }, s.zeit))));
    const zeilen = daten.tage.map((t) => h('tr', null, h('th', null, t.name), ...t.zellen.map((z) => {
      const kl = ['pa-zelle'];
      if (sicht === 'gang') { if (z.keine) kl.push('keine'); if (z.offen && !z.keine) kl.push('offen'); if (z.schlecht?.length) kl.push('schlecht'); }
      else kl.push(z.moeglich ? 'moeglich' : 'gesperrt');
      if (akt && akt.tag === t.tag && akt.nach === z.nach) kl.push('aktiv');
      const td = h('td', { class: kl.join(' '), title: z.schlecht?.length ? `Schlecht bewertet: ${z.schlecht.join(', ')}` : '' }, z.text);
      td.addEventListener('click', () => { akt = { tag: t.tag, nach: z.nach }; zeichnen(); });
      td.addEventListener('dblclick', () => { akt = { tag: t.tag, nach: z.nach }; if (sicht === 'gang' && schreiben) eingeben(td, z.text); });
      td.addEventListener('contextmenu', (e) => {
        e.preventDefault();
        akt = { tag: t.tag, nach: z.nach };
        zeichnen();
        if (sicht !== 'gang' || !schreiben) return;
        menue(e.clientX, e.clientY, [
          { text: 'Lehrervorschlag …', symbol: 'lehrer', aktion: vorschlag },
          { text: 'Eingeben …', aktion: () => eingeben(null, z.text) },
          { text: 'Aufsicht löschen', symbol: 'loeschen', aktion: zelleLoeschen },
          { text: z.keine ? 'Aufsicht (???)' : 'Keine Aufsicht', aktion: () => teile(0) },
          { text: 'Aufsicht teilen', aktion: () => teile(1) },
          { text: 'Teilung entfernen', aktion: () => teile(-1), deaktiviert: z.teile < 2 },
        ]);
      });
      return td;
    })));
    raster.replaceChildren(h('table', { class: 'pa-tabelle' }, h('thead', null, kopf), h('tbody', null, zeilen)));
    seite.replaceChildren(h('table', { class: 'pa-tabelle klein' },
      h('thead', null, h('tr', null, ['Lehrer', 'PA Max (Min)', 'PA Ist (Min)', 'PA Anz', 'PA-Vertr.', 'Ist-Max', 'Geschl.'].map((x) => h('th', null, x)))),
      h('tbody', null, daten.lehrer.map((l) => h('tr', { class: 'pa-lehrerzeile', ondblclick: () => { sicht = 'lehrer'; elementeFuellen(); element.value = l.id; neu(); } },
        h('td', null, l.kurz), h('td', null, l.max), h('td', null, l.ist), h('td', null, l.anzahl), h('td', null, l.vertretungen), h('td', { class: l.ueber ? 'pa-ueber' : '' }, l.differenz), h('td', null, l.geschlecht))))));
  }

  async function eingeben(td, alt) {
    const t = await (async () => {
      const ein = h('input', { class: 'eingabe', value: alt === '???' ? '' : alt.replace(/\?\?\?/g, '').replace(/,\s*,/g, ',').replace(/^,|,$/g, ''), placeholder: 'Kurznamen, z. B. MÜL, SCH (geteilte Aufsicht)' });
      const ok = await dialog({ titel: 'Pausenaufsicht', inhalt: h('div', { class: 'formular' }, h('label', null, 'Lehrer'), ein), knoepfe: [{ text: 'OK', wert: true, primaer: true }, { text: 'Abbrechen', wert: null }] });
      return ok ? ein.value : null;
    })();
    if (t === null) return;
    daten = await api.put(api.dok('/pausenaufsicht/zelle'), { gang: Number(element.value), tag: akt.tag, nach: akt.nach, lehrer: t }, { quelle: f.id });
    zeichnen();
  }

  async function zelleLoeschen() {
    if (!akt) return;
    await api.post(api.dok('/pausenaufsicht/loeschen'), { sicht, id: Number(element.value), tag: akt.tag, nach: akt.nach }, { quelle: f.id });
    neu();
  }
  async function teile(delta) {
    if (!akt || sicht !== 'gang') { hinweis('Bitte im Reiter Gang eine Aufsicht wählen.', 'warnung'); return; }
    daten = await api.post(api.dok('/pausenaufsicht/teile'), { gang: Number(element.value), tag: akt.tag, nach: akt.nach, delta }, { quelle: f.id });
    zeichnen();
  }

  async function vorschlag() {
    if (sicht !== 'gang' || !akt) { hinweis('Bitte im Reiter Gang eine Aufsicht wählen.', 'warnung'); return; }
    const gang = Number(element.value);
    const v = await api.get(api.dok(`/pausenaufsicht/vorschlag?gang=${gang}&tag=${akt.tag}&nach=${akt.nach}`));
    let gewaehlt = null;
    const huelle = h('div', { class: 'pa-vorschlag' });
    const ok = dialog({ titel: v.titel, breite: '960px', inhalt: h('div', null, v.ersetzt ? h('p', { class: 'leise' }, `Alle Teile belegt – ersetzt wird ${v.ersetzt}.`) : null, huelle),
      knoepfe: [{ text: 'Einteilen', wert: true, primaer: true }, { text: 'Abbrechen', wert: null }] });
    const t = new Tabelle(huelle, {
      zeilenKlasse: (z) => (z.schlecht ? 'pa-schlecht' : ''),
      beiAuswahl: (_, aktiv) => { gewaehlt = aktiv; },
      beiDoppelklick: (z) => { gewaehlt = z; huelle.closest('.dialog')?.querySelector('.knopf.primaer')?.click(); },
      spalten: v.spalten.map((titel, i) => ({ schluessel: `v${i}`, titel, breite: i === 0 ? 60 : 62, art: 'NurLesen', editierbar: false, wert: (z) => z.werte[i] })),
    });
    t.daten(v.zeilen.map((z) => ({ ...z, title: z.verletzt.join('\n') })));
    gewaehlt = v.zeilen[0] || null;
    if (!(await ok) || !gewaehlt) return;
    daten = await api.post(api.dok('/pausenaufsicht/zuordnen'), { gang, tag: akt.tag, nach: akt.nach, lehrer: gewaehlt.id }, { quelle: f.id });
    zeichnen();
  }

  async function optimieren() {
    const nurGang = h('input', { type: 'checkbox', disabled: sicht !== 'gang' });
    const schlecht = h('input', { type: 'checkbox' });
    const kurz = h('input', { class: 'eingabe', type: 'number', min: 0, max: 60, value: 0, style: { width: '70px' } });
    const ok = await dialog({ titel: 'Optimierung Pausenaufsichten', breite: '440px', inhalt: h('div', { class: 'upw-spalte' },
      h('label', { class: 'kontrolle' }, nurGang, 'Nur aktuellen Gang'), h('label', { class: 'kontrolle' }, schlecht, 'Schlechte Einteilungen vorher löschen'),
      h('label', { class: 'kontrolle' }, kurz, 'Pausen ab Minuten'),
      h('p', { class: 'leise' }, 'Es werden nur offene Aufsichten (???) besetzt. Lehrer mit PA Max = 0, gesperrte Lehrer und unpassendes Geschlecht sind ausgeschlossen.')),
    knoepfe: [{ text: 'OK', wert: true, primaer: true }, { text: 'Abbrechen', wert: null }] });
    if (!ok) return;
    const weg = laden(f.inhalt);
    try {
      const r = await api.post(api.dok('/pausenaufsicht/optimieren'), { schlechteLoeschen: schlecht.checked, gang: nurGang.checked ? Number(element.value) : null, kuerzestePause: Number(kurz.value) || 0 });
      hinweis(r.text, 'ok');
    } finally { weg(); }
    neu();
  }

  async function gewichtung() {
    const g = await api.get(api.dok('/pausenaufsicht/gewichtung'));
    const max = h('input', { class: 'eingabe', type: 'number', min: 1, max: 5, value: g.maxProTag, style: { width: '60px' } });
    const regler = g.kriterien.map((k) => ({ k, el: h('input', { type: 'range', min: 0, max: 5, value: k.wert }) }));
    const ok = await dialog({ titel: 'Gewichtung Pausenaufsichten', breite: '620px', inhalt: h('div', { class: 'pa-gewichtung' },
      h('label', null, 'Max. Pausenaufsichten pro Tag'), max, h('div', { class: 'leise pa-skala' }, 'unwichtig ← → extrem wichtig (5 = nie verletzen)'),
      ...regler.flatMap(({ k, el }) => [h('label', null, k.text), el])),
    knoepfe: [{ text: 'OK', wert: true, primaer: true }, { text: 'Abbrechen', wert: null }] });
    if (!ok) return;
    await api.put(api.dok('/pausenaufsicht/gewichtung'), { maxProTag: Number(max.value) || 1, werte: Object.fromEntries(regler.map(({ k, el }) => [k.schluessel, Number(el.value)])) });
    hinweis('Gewichtung gespeichert.', 'ok');
    neu();
  }

  async function neu() {
    const id = Number(element.value);
    if (!id) { daten = null; zeichnen(); return; }
    try { daten = await api.get(api.dok(`/pausenaufsicht/${sicht}/${id}`)); } catch (e) { daten = null; fehlerMelden(e); }
    zeichnen();
  }

  f.an('daten', (e) => { if (e.quelle !== f.id && betrifft(e, 'Stammdaten', 'Stundenplan', 'Einstellungen', 'Vertretung')) start(); });
  async function start() {
    try { liste = await api.get(api.dok('/pausenaufsicht')); } catch (e) { fehlerMelden(e); }
    elementeFuellen();
    await neu();
  }
  start();
}

// ---------------------------------------------------------------------------------------------- Vertretung der Aufsichten

function vertretungFenster(f) {
  f.titel('Vertretung der Pausenaufsichten');
  const schreiben = darfSchreiben();
  const start = zustand.vertretungsdatum || zustand.datum || new Date().toISOString().slice(0, 10);
  const datum = h('input', { class: 'eingabe', type: 'date', value: typeof start === 'string' ? start.slice(0, 10) : start });
  f.werkzeuge.append(h('span', null, 'Datum'), datum);
  f.werkzeug({ text: 'Plan', symbol: 'pausenaufsicht', titel: 'Wöchentliche Pausenaufsichten', aktion: () => fensterverwaltung.oeffnen('pausenaufsicht', {}) });
  const flaeche = h('div', { class: 'pa-vertretung' });
  f.inhalt.append(flaeche);

  async function neu() {
    const weg = laden(f.inhalt);
    try { zeichnen(await api.get(api.dok(`/pausenaufsicht/vertretung/${datum.value}`))); } catch (e) { fehlerMelden(e); } finally { weg(); }
  }
  function zeichnen(d) {
    f.fuss.replaceChildren(h('span', null, `${d.datumText}: ${d.zeilen.length} Aufsichtsvertretungen, ${d.zeilen.filter((z) => z.offen).length} offen`), d.unterrichtstag ? null : h('span', { class: 'leise' }, ' · kein Unterrichtstag'));
    if (!d.zeilen.length) { flaeche.replaceChildren(h('p', { class: 'leise pa-leer' }, 'Keine Pausenaufsichtsvertretungen an diesem Tag (entstehen automatisch, wenn eingeteilte Lehrkräfte abwesend sind).')); return; }
    flaeche.replaceChildren(h('table', { class: 'pa-tabelle' },
      h('thead', null, h('tr', null, ['Pause', 'Gang', 'Lehrer (abwesend)', 'Vertretung', 'Text', 'Kandidaten (Zähler, Aufsichten)'].map((x) => h('th', null, x)))),
      h('tbody', null, d.zeilen.map((z) => {
        const wahl = h('select', { class: 'eingabe', disabled: !schreiben },
          h('option', { value: '' }, z.offen ? '??? (offen)' : '– Vertretung aufheben –'),
          z.vertreterId && !z.kandidaten.some((k) => k.id === z.vertreterId) ? h('option', { value: z.vertreterId, selected: true }, z.vertreter) : null,
          ...z.kandidaten.map((k) => h('option', { value: k.id, selected: k.id === z.vertreterId }, `${k.kurz} (${k.zaehler}, ${k.aufsichten})`)));
        wahl.addEventListener('change', async () => {
          try { zeichnen(await api.post(api.dok(`/pausenaufsicht/vertretung/${datum.value}`), { vertretung: z.id, lehrer: wahl.value ? Number(wahl.value) : null }, { quelle: f.id })); } catch (e) { fehlerMelden(e); neu(); }
        });
        return h('tr', { class: z.offen ? 'pa-offen' : '' }, h('td', null, z.pause), h('td', null, z.gang), h('td', null, z.lehrer), h('td', null, wahl), h('td', null, z.text), h('td', { class: 'leise' }, z.kandidaten.slice(0, 6).map((k) => k.kurz).join(', ')));
      }))));
  }
  datum.addEventListener('change', neu);
  f.an('datum', (dt) => { if (dt) { datum.value = String(dt).slice(0, 10); neu(); } });
  f.an('daten', (e) => { if (e.quelle !== f.id && betrifft(e, 'Vertretung', 'Stundenplan')) neu(); });
  neu();
}

const oeffnen = () => { if (zustand.dokument) fensterverwaltung.oeffnen('pausenaufsicht', {}); };

export default {
  id: 'pausenaufsicht',
  css: ['css/module/pausenaufsicht.css'],
  fenster: {
    pausenaufsicht: { titel: 'Pausenaufsichten', symbol: 'pausenaufsicht', breite: 1060, hoehe: 520, einzig: () => 'pause', erstellen: planFenster },
    'pausenaufsicht.vertretung': { titel: 'Vertretung der Pausenaufsichten', symbol: 'pausenaufsicht', breite: 900, hoehe: 420, einzig: () => 'pavertretung', erstellen: vertretungFenster },
  },
  befehle: {
    'pausenaufsicht.oeffnen': oeffnen,
    'pausenaufsicht.vertretung': () => { if (zustand.dokument) fensterverwaltung.oeffnen('pausenaufsicht.vertretung', {}); },
  },
  ribbon: [
    { id: 'start.pausenaufsichten', reiter: 'Start', gruppe: 'Module', text: 'Pausenaufsichten', symbol: 'pausenaufsicht', befehl: 'pausenaufsicht.oeffnen', braucht: 'dokument' },
    {
      id: 'vertretung.pausenaufsichten', reiter: 'Vertretungsplanung', gruppe: 'Planung', text: 'Pausen-aufsichten', symbol: 'pausenaufsicht', gross: true, braucht: 'dokument',
      menue: [
        { text: 'Vertretung der Pausenaufsichten …', symbol: 'vertretung', befehl: 'pausenaufsicht.vertretung' },
        { text: 'Pausenaufsichten (Wochenplan) …', symbol: 'pausenaufsicht', befehl: 'pausenaufsicht.oeffnen' },
      ],
    },
  ],
};
