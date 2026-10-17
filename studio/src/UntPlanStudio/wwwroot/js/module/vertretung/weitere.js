// Weitere Fenster und Dialoge der Vertretungsplanung: Sondereinsätze (mehrere Zeilen, Klassenkonferenz), Vertretungsautomatik,
// Einstellungen Vertretungsplanung (Vertretungszeitraster, Zähler, Vorschlag, Allgemein, Farben und Bezeichnungen),
// Bereitschaften des Tages, Bereitschaftsplanung (Raster mit Bedarf, Vorschlag, Automatik) und Vormerkungen.
import { h, leeren } from '../../kern/dom.js';
import { api } from '../../kern/api.js';
import { darfSchreiben } from '../../kern/zustand.js';
import { betrifft } from '../../kern/ereignisse.js';
import { dialog, hinweis, fehlerMelden, frage, formular, meldung } from '../../kern/dialog.js';
import { symbol } from '../../kern/symbole.js';
import { vpfad, planungsdatum, datumText, datumLesen, auswahlMelden } from './kern.js';
import { reiter, listenTabelle } from './steuer.js';
import { TYP_VORMERKUNG } from './plan.js';

// ---------------------------------------------------------------------------------------------
// Sondereinsätze

/** Dialog „Sondereinsätze“: Datum, Stunde von/bis, Klassen, Lehrer, Klassenkonferenz; darunter mehrere Zeilen direkt eingeben. */
export async function sondereinsatzDialog({ datum, stunde, klassen = '', lehrer = '' } = {}) {
  const d0 = datum || planungsdatum();
  const st = stunde || 1;
  const koerper = h('tbody');
  const zeile = (w = {}) => {
    const feld = (name, wert, breite) => h('input', { class: 'eingabe', name, value: wert ?? '', style: breite ? { width: breite } : null });
    const tr = h('tr', null,
      h('td', null, feld('datum', datumText(w.datum || d0), '96px')), h('td', null, feld('sv', w.sv ?? st, '46px')), h('td', null, feld('sb', w.sb ?? st, '46px')),
      h('td', null, feld('lehrer', w.lehrer ?? lehrer, '110px')), h('td', null, feld('klassen', w.klassen ?? klassen, '110px')), h('td', null, feld('fach', w.fach, '70px')),
      h('td', null, feld('raum', w.raum, '70px')), h('td', null, feld('gruppe', w.gruppe, '90px')), h('td', null, feld('text', w.text, '180px')),
      h('td', null, h('button', { type: 'button', class: 'wz-knopf', title: 'Zeile löschen', onclick: () => { tr.remove(); if (!koerper.children.length) koerper.append(zeile()); } }, symbol('loeschen'))));
    tr.addEventListener('input', () => { if (tr === koerper.lastElementChild) koerper.append(zeile({ datum: tr.querySelector('[name=datum]').value ? datumLesen(tr.querySelector('[name=datum]').value, d0) : d0, sv: tr.querySelector('[name=sv]').value, sb: tr.querySelector('[name=sb]').value, lehrer: '', klassen: '' })); });
    return tr;
  };
  koerper.append(zeile());
  const kk = h('button', { type: 'button', class: 'knopf', title: 'Klassenkonferenz: alle Lehrkräfte, die die Klassen der aktuellen Zeile unterrichten' }, 'Klassenkonferenz');
  kk.addEventListener('click', async () => {
    const tr = koerper.querySelector('tr:focus-within') || koerper.firstElementChild;
    let kl = tr.querySelector('[name=klassen]').value.trim();
    if (!kl) { const w = await formular('Klassenkonferenz für Klasse(n)', [{ name: 'k', titel: 'Klassen', pflicht: true }]); if (!w) return; kl = w.k; tr.querySelector('[name=klassen]').value = kl; }
    const r = await api.get(vpfad(`/klassenkonferenz?klassen=${encodeURIComponent(kl)}`));
    tr.querySelector('[name=lehrer]').value = r.lehrer;
    if (!tr.querySelector('[name=text]').value) tr.querySelector('[name=text]').value = 'Klassenkonferenz';
  });
  const inhalt = h('div', { class: 'vt-sondereinsatz' },
    h('p', { class: 'leise' }, 'Je Zeile ein Sondereinsatz für den Stundenbereich. Kollidierender Unterricht der Lehrkräfte wird zur offenen Vertretung bzw. entfällt laut Vertretungszeitraster, kollidierender Unterricht der Klassen entfällt.'),
    h('div', { class: 'vt-tabwrap' }, h('table', { class: 'vt-eingabetabelle' }, h('thead', null, h('tr', null, ['Datum', 'Std. von', 'Std. bis', 'Lehrer', 'Klassen', 'Fach', 'Raum', 'Schülergruppe', 'Text', ''].map((t) => h('th', null, t)))), koerper)),
    h('div', { class: 'vt-zeile' }, kk));
  const ok = await dialog({ titel: 'Sondereinsätze', inhalt, breite: '940px', knoepfe: [{ text: 'OK', wert: true, primaer: true }, { text: 'Abbrechen', wert: null }] });
  if (!ok) return null;
  const zeilen = [...koerper.children].map((tr) => Object.fromEntries([...tr.querySelectorAll('input')].map((i) => [i.name, i.value.trim()])))
    .filter((z) => z.lehrer || z.klassen)
    .map((z) => ({ datum: datumLesen(z.datum, d0), sv: Number(z.sv) || 1, sb: Number(z.sb) || Number(z.sv) || 1, lehrer: z.lehrer, klassen: z.klassen, fach: z.fach, raum: z.raum, gruppe: z.gruppe, text: z.text }));
  if (!zeilen.length) return null;
  const r = await api.post(vpfad('/sondereinsaetze'), { zeilen });
  hinweis(`${r.anzahl} Sondereinsatz-Stunde(n) angelegt – Strg+Z macht es rückgängig.`, 'ok');
  return r;
}

// ---------------------------------------------------------------------------------------------
// Vertretungsautomatik

export async function automatikDialog(von, bis) {
  von = von || planungsdatum();
  bis = bis || von;
  const info = await api.get(vpfad(`/automatik?von=${von}&bis=${bis}`));
  const vonEl = h('input', { class: 'eingabe', type: 'date', value: von });
  const bisEl = h('input', { class: 'eingabe', type: 'date', value: bis });
  const methode = h('select', { class: 'eingabe' }, info.methoden.map((m) => h('option', { value: m.wert }, m.text)));
  const offen = h('div', { class: 'vt-offen' });
  const zeigen = (o) => { leeren(offen).append(h('div', null, `Vertretungen: ${o.vertretungen}`), h('div', null, `Raumvertretungen: ${o.raumvertretungen}`), h('div', null, `Pausenaufsichten: ${o.pausenaufsichten}`)); };
  zeigen(info.offen);
  const neu = async () => zeigen((await api.get(vpfad(`/automatik?von=${vonEl.value}&bis=${bisEl.value || vonEl.value}`))).offen);
  vonEl.addEventListener('change', neu);
  bisEl.addEventListener('change', neu);
  const inhalt = h('div', { class: 'formular' }, h('label', null, 'Von'), vonEl, h('label', null, 'Bis'), bisEl, h('label', null, 'Methode'), methode,
    h('label', null, 'offene Vertretungen'), offen,
    h('div', { class: 'hinweis voll' }, 'Die offenen Vertretungen des Zeitraums werden nacheinander bearbeitet (bester Vertreter des Vorschlags, sonst Entfall, wenn sinnvoll). Strg+Z macht alles rückgängig.'));
  const ok = await dialog({ titel: 'Vertretungsautomatik', inhalt, breite: '520px', knoepfe: [{ text: 'Start', wert: true, primaer: true }, { text: 'Abbrechen', wert: null }] });
  if (!ok) return;
  const r = await api.post(vpfad('/automatik'), { von: vonEl.value, bis: bisEl.value || vonEl.value, methode: methode.value });
  meldung(`${r.bearbeitet} Vertretung(en) bearbeitet, ${r.offen} noch offen.`, 'Vertretungsautomatik');
}

// ---------------------------------------------------------------------------------------------
// Einstellungen Vertretungsplanung

const REGEL = ['Vertreten', 'Entfall', 'Eigenverantwortlich'];
const REGEL_TEXT = { Vertreten: '', Entfall: 'E', Eigenverantwortlich: 'EVA' };

export async function einstellungenDialog(seite = 0) {
  const ein = await api.get(vpfad('/einstellungen'));
  const e = structuredClone(ein.einstellungen);
  const klassen = structuredClone(ein.klassen);
  const schreiben = darfSchreiben();
  const seiten = [];
  // --- Vertretungszeitraster ---
  const klasseSel = h('select', { class: 'eingabe' }, h('option', { value: '' }, '(gesamte Schule)'), klassen.map((k) => h('option', { value: k.id }, k.kurz)));
  const raster = h('table', { class: 'vt-zeitraster' });
  const aktuell = () => (klasseSel.value ? klassen.find((k) => String(k.id) === klasseSel.value).zeitraster : e.zeitraster);
  const rasterZeichnen = () => {
    const r = aktuell();
    leeren(raster).append(h('tr', null, h('th', null, 'Std.'), ein.tage.map((t) => h('th', null, t))));
    for (let s = 1; s <= ein.stunden; s++) {
      raster.append(h('tr', null, h('th', null, String(s)), ein.tage.map((_, t) => {
        const key = String(t * 100 + s);
        const td = h('td', { class: `vt-regel-${r[key] || 'Vertreten'}` }, REGEL_TEXT[r[key] || 'Vertreten']);
        td.addEventListener('click', () => {
          const neu = REGEL[(REGEL.indexOf(r[key] || 'Vertreten') + 1) % 3];
          if (neu === 'Vertreten') delete r[key]; else r[key] = neu;
          td.className = `vt-regel-${neu}`;
          td.textContent = REGEL_TEXT[neu];
        });
        return td;
      })));
    }
  };
  klasseSel.addEventListener('change', rasterZeichnen);
  rasterZeichnen();
  seiten.push(h('div', null, h('div', { class: 'vt-zeile' }, h('label', null, 'Zeitraster für: '), klasseSel), raster,
    h('p', { class: 'leise' }, 'Klick wechselt: leer = vertreten · E = Unterricht entfällt bei Absenz der Lehrkraft · EVA = eigenverantwortliches Arbeiten. Ein Klassenzeitraster hat Vorrang vor dem Zeitraster der Schule.')));
  // --- Vertretungszähler ---
  const z = e.zaehler;
  const cb = (obj, k, text) => { const i = h('input', { type: 'checkbox', checked: !!obj[k] }); i.addEventListener('change', () => { obj[k] = i.checked; }); return h('label', { class: 'voll' }, i, ` ${text}`); };
  const sel = (obj, k, optionen) => { const s = h('select', { class: 'eingabe' }, optionen.map(([w, t]) => h('option', { value: w, selected: obj[k] === w }, t))); s.addEventListener('change', () => { obj[k] = s.value; }); return s; };
  const txt = (obj, k) => { const i = h('input', { class: 'eingabe', value: obj[k] ?? '' }); i.addEventListener('change', () => { obj[k] = i.value; }); return i; };
  const zahl = (obj, k, max = 99) => { const i = h('input', { class: 'eingabe', type: 'number', min: 0, max, value: obj[k] ?? 0 }); i.addEventListener('change', () => { obj[k] = Math.max(0, Math.min(max, Number(i.value) || 0)); }); return i; };
  seiten.push(h('div', { class: 'formular' },
    cb(z, 'vertretungenZaehlen', 'Vertretungen zählen (+)'), cb(z, 'sondereinsaetzeZaehlen', 'Sondereinsätze zählen (+)'), cb(z, 'entfaelleZaehlen', 'Entfälle zählen (–)'),
    cb(z, 'freisetzungenZaehlen', 'Freisetzungen zählen (–)'), cb(z, 'pausenaufsichtenZaehlen', 'Pausenaufsicht-Vertretungen zählen'), cb(z, 'betreuungenHalb', 'Betreuungen zählen halb'),
    cb(z, 'entfallNurMitGrund', 'Entfälle nur zählen, wenn der Absenzgrund „Entfall zählen“ gesetzt ist'), cb(z, 'stattVertretungNeutral', 'Statt-Vertretungen neutral (zählen nicht)'),
    cb(z, 'klausurenNeutral', 'Klausuraufsichten neutral'),
    h('label', null, 'Durchrechnungszeitraum'), sel(z, 'zeitraum', [['Woche', 'Woche'], ['Monat', 'Monat'], ['Halbjahr', 'Halbjahr'], ['Schuljahr', 'Schuljahr'], ['Benutzerdefiniert', 'benutzerdefiniert']]),
    h('label', null, 'Wie zählen'), sel(z, 'wieZaehlen', [['Anzahl', 'Anzahl der Stunden'], ['Werte', 'Werte (Faktor des Unterrichts)']]),
    h('label', null, 'Kennzeichen „nicht zählen“'), txt(z, 'nichtZaehlenKennzeichen'), h('label', null, 'Kennzeichen „bezahlt“'), txt(z, 'bezahltKennzeichen')));
  // --- Vertretungsvorschlag ---
  const v = e.vorschlag;
  seiten.push(h('div', { class: 'formular' },
    h('div', { class: 'voll fett' }, 'Bonus (je höher, desto eher wird die Lehrkraft vorgeschlagen)'),
    h('label', null, 'Freisetzung (Klasse der Lehrkraft abwesend)'), zahl(v, 'freisetzung'), h('label', null, 'Bereitschaft'), zahl(v, 'bereitschaft'),
    h('label', null, 'Lehrkraft unterrichtet die Klasse'), zahl(v, 'hatKlasse'), h('label', null, 'Lehrkraft unterrichtet das Fach'), zahl(v, 'hatFach'),
    h('div', { class: 'voll fett' }, 'Schlechtpunkte (Faktoren)'),
    h('label', null, 'je Unterrichtsstunde am Tag'), zahl(v, 'unterrichtsstundenAmTag'), h('label', null, 'je Merkerstufe'), zahl(v, 'merkerFaktor'),
    h('label', null, 'je Stufe Sperrvermerk'), zahl(v, 'sperreFaktor'), h('label', null, 'je Punkt im Vertretungszähler'), zahl(v, 'zaehlerFaktor'),
    h('div', { class: 'voll fett' }, 'Anzeige'),
    h('label', null, 'rot, wenn Merker größer als'), zahl(v, 'rotWennMerkerGroesser', 9), h('label', null, 'nicht anzeigen, wenn Merker größer als'), zahl(v, 'nichtAnzeigenWennMerkerGroesser', 9),
    h('label', null, 'rot, wenn Zähler größer als'), zahl(v, 'rotWennZaehlerGroesser'),
    cb(v, 'gesperrteNichtAnzeigen', 'Gesperrte (Zeitwunsch -3) nicht anzeigen'), cb(v, 'klasseAuchFuerKlassenteil', 'Klasse auch für Klassenteil'), cb(v, 'langname', 'Langname anzeigen'),
    cb(v, 'raumgewicht4Beachten', 'Raumgewicht 4 bei Vorziehungen'), cb(v, 'nurSpaeterenUnterricht', 'Vorziehung nur aus späterem Unterricht'), cb(v, 'nurDieselbeWoche', 'Vorziehung nur aus derselben Woche'),
    h('label', null, 'Vorziehungen'), sel(v, 'vorziehungen', [['GemaessVertretungszeitraster', 'gemäß Vertretungszeitraster'], ['AusRandstunden', 'aus Randstunden'], ['AlleVerlegungen', 'alle Verlegungen']])));
  // --- Allgemein ---
  const eva = h('input', { class: 'eingabe', value: ein.evaRaum || '', placeholder: 'Raum (Kurzname)' });
  seiten.push(h('div', { class: 'formular' }, h('label', null, 'Raum für EVA'), eva,
    cb(e, 'lehrerRaumPrinzip', 'Lehrer-Raum-Prinzip (Raum des Vertreters verwenden)'), cb(e, 'freisetzungPausenaufsicht', 'Freisetzungen erzeugen Pausenaufsicht-Vertretungen'),
    cb(e, 'mitAktuellemDatumStarten', 'Vertretungsplanung mit aktuellem Datum starten')));
  // --- Farben und Bezeichnungen ---
  const farbZeilen = ein.arten.map((a) => {
    const t = h('input', { class: 'eingabe', value: a.text, placeholder: a.standard });
    const fa = h('input', { type: 'color', value: a.farbe || '#ffffff' });
    const ohne = h('input', { type: 'checkbox', checked: !a.farbe, title: 'keine Farbe' });
    return { a, t, fa, ohne, tr: h('tr', null, h('td', null, a.standard), h('td', null, t), h('td', null, fa), h('td', null, h('label', null, ohne, ' keine'))) };
  });
  seiten.push(h('div', { class: 'vt-tabwrap' }, h('table', { class: 'vt-eingabetabelle' }, h('thead', null, h('tr', null, ['Vertretungsart', 'Bezeichnung', 'Farbe', ''].map((x) => h('th', null, x)))), h('tbody', null, farbZeilen.map((x) => x.tr)))));
  const titel = ['Vertretungszeitraster', 'Vertretungszähler', 'Vertretungsvorschlag', 'Allgemein', 'Farben und Bezeichnungen'];
  const r = reiter(titel, (i) => seiten.forEach((s, j) => { s.hidden = i !== j; }), seite);
  seiten.forEach((s, j) => { s.hidden = j !== seite; s.classList.add('vt-seite'); });
  const inhalt = h('div', { class: 'vt-einstellungen' }, r.el, ...seiten);
  const ok = await dialog({ titel: 'Einstellungen Vertretungsplanung', inhalt, breite: '720px', knoepfe: schreiben ? [{ text: 'OK', wert: true, primaer: true }, { text: 'Abbrechen', wert: null }] : [{ text: 'Schließen', wert: null }] });
  if (!ok) return false;
  e.bezeichnungen = Object.fromEntries(farbZeilen.filter((x) => x.t.value.trim()).map((x) => [x.a.art, x.t.value.trim()]));
  e.farben = Object.fromEntries(farbZeilen.filter((x) => !x.ohne.checked).map((x) => [x.a.art, (0xff000000 | parseInt(x.fa.value.slice(1), 16)) >> 0]));
  await api.put(vpfad('/einstellungen'), { einstellungen: e, evaRaum: eva.value.trim(), klassen: klassen.map((k) => ({ id: k.id, zeitraster: k.zeitraster })) });
  hinweis('Einstellungen der Vertretungsplanung gespeichert.', 'ok');
  return true;
}

// ---------------------------------------------------------------------------------------------
// Bereitschaften des Tages

function bereitschaftenFenster(f) {
  const t = listenTabelle(f.inhalt, {
    leerText: 'Keine Bereitschaften an diesem Tag (Fach mit Kennzeichen „Bereitschaft“ im Stundenplan).',
    zeilenKlasse: (z) => `vt-b-${z.status}`,
    spalten: [
      { titel: 'Std.', breite: 40, klasse: 'mitte', wert: (z) => String(z.stunde) }, { titel: 'Lehrer', breite: 80, wert: (z) => z.lehrer },
      { titel: 'Bereitschaft', breite: 90, wert: (z) => z.fach }, { titel: 'eingesetzt für', wert: (z) => z.eingesetzt }, { titel: 'Zähler', breite: 56, klasse: 'rechts', wert: (z) => z.zaehler },
    ],
    beiWahl: (z) => { if (z.vertretungen?.length) auswahlMelden({ id: z.vertretungen[0], ids: z.vertretungen, zeile: { v: z.vertretungen[0], vertreter: z.lehrerId }, quelle: f.id }); },
  });
  async function laden() {
    if (!planungsdatum()) return;
    const d = await api.get(vpfad(`/bereitschaften?datum=${planungsdatum()}`));
    f.titel(d.titel);
    t.setzen(d.zeilen);
  }
  f.an('vertretung.datum', () => laden().catch(fehlerMelden));
  f.an('daten', (e) => { if (betrifft(e, 'Vertretung', 'Stundenplan', 'Stammdaten')) laden().catch(() => {}); });
  laden().catch(fehlerMelden);
}

// ---------------------------------------------------------------------------------------------
// Bereitschaftsplanung (Modul)

function bereitschaftsplanungFenster(f) {
  const schreiben = darfSchreiben();
  let daten = null;
  let gewaehlt = null;
  const raster = h('table', { class: 'vt-bpraster' });
  const zelleTitel = h('span', { class: 'fett' });
  const bedarf = h('input', { class: 'eingabe', type: 'number', min: 0, max: 20, style: { width: '60px' }, disabled: !schreiben });
  const eingeteilt = h('select', { class: 'eingabe', size: 6 });
  const rechts = h('div', { class: 'vt-bp-rechts' });
  f.inhalt.append(h('div', { class: 'vt-bp' }, h('div', { class: 'vt-bp-links' }, raster), rechts));
  const entfernen = h('button', { type: 'button', class: 'knopf', disabled: !schreiben }, 'Entfernen');
  const eintragen = h('button', { type: 'button', class: 'knopf', disabled: !schreiben }, 'Eintragen');
  rechts.append(h('div', { class: 'vt-zeile' }, zelleTitel, h('label', null, ' Bedarf: '), bedarf, eintragen, entfernen), h('div', null, 'Eingeteilt:'), eingeteilt, h('div', null, 'Lehrervorschlag (Doppelklick = eintragen):'));
  const vorschlag = listenTabelle(rechts, {
    leerText: 'Bitte links eine Stunde wählen',
    spalten: [{ titel: 'Lehrer', breite: 60, wert: (k) => k.kurz }, { titel: 'Pkt.', breite: 40, klasse: 'rechts', wert: (k) => String(k.punkte) }, { titel: 'Ber./Woche', breite: 70, klasse: 'rechts', wert: (k) => String(k.woche) },
      { titel: 'freies Dep.', breite: 70, klasse: 'rechts', wert: (k) => k.dep }, { titel: 'Lage', wert: (k) => k.hinweis }],
    beiDoppelklick: (k) => aktion('setzen', { lehrer: k.lehrer }),
  });
  eintragen.addEventListener('click', () => vorschlag.aktiv && aktion('setzen', { lehrer: vorschlag.aktiv.lehrer }));
  entfernen.addEventListener('click', () => eingeteilt.value && aktion('entfernen', { lehrer: Number(eingeteilt.value) }));
  bedarf.addEventListener('change', () => gewaehlt && aktion('bedarf', { anzahl: Number(bedarf.value) || 0 }));

  async function aktion(a, x = {}) {
    const r = await api.post(vpfad(`/bereitschaftsplanung/${a}`), { tag: gewaehlt?.tag ?? 0, stunde: gewaehlt?.stunde ?? 1, ...x }, { quelle: f.id });
    if (a === 'automatisch') hinweis(`${r.anzahl} Bereitschaft(en) eingetragen${r.fehlt ? `, ${r.fehlt} fehlen noch` : ''}.`, 'ok');
    await laden();
  }
  function zeichnen() {
    leeren(raster).append(h('tr', null, h('th', null, 'Std.'), daten.tage.map((t) => h('th', null, t))));
    daten.stunden.forEach((s, j) => raster.append(h('tr', null, h('th', null, s), daten.tage.map((_, t) => {
      const z = daten.zellen[t][j];
      const td = h('td', { class: `${z.verfuegbar ? '' : 'gesperrt'} ${z.bedarf > z.lehrer.length ? 'fehlt' : z.bedarf && z.bedarf <= z.lehrer.length ? 'voll' : ''} ${gewaehlt?.tag === t && gewaehlt?.stunde === j + 1 ? 'aktiv' : ''}` },
        z.bedarf || z.lehrer.length ? h('span', null, h('b', null, `${z.lehrer.length}/${z.bedarf}`), ` ${z.lehrer.map((l) => l.kurz).join(', ')}`) : '');
      td.addEventListener('click', () => waehlen(t, j + 1));
      return td;
    }))));
    f.titel(`Bereitschaftsplanung – ${daten.anzahl} Bereitschaften, Bedarf ${daten.bedarf}`);
  }
  async function waehlen(tag, stunde) {
    gewaehlt = { tag, stunde };
    zeichnen();
    const z = daten.zellen[tag][stunde - 1];
    zelleTitel.textContent = `${daten.tage[tag]}, ${daten.stunden[stunde - 1]}. Std.`;
    bedarf.value = z.bedarf;
    leeren(eingeteilt).append(...z.lehrer.map((l) => h('option', { value: l.id }, l.kurz)));
    vorschlag.setzen(await api.get(vpfad(`/bereitschaftsplanung/vorschlag?tag=${tag}&stunde=${stunde}`)));
  }
  async function laden() {
    daten = await api.get(vpfad('/bereitschaftsplanung'));
    zeichnen();
    if (gewaehlt) await waehlen(gewaehlt.tag, gewaehlt.stunde);
    doppelt.classList.toggle('gedrueckt', daten.nurDoppelstunden);
  }
  if (schreiben) {
    f.werkzeug({ text: 'Automatisch besetzen', symbol: 'optimierung', titel: 'Fehlende Bereitschaften automatisch besetzen', aktion: () => aktion('automatisch') });
    f.werkzeug({ text: 'Bedarf Vormittag', symbol: 'vertretung', titel: 'Bedarf für alle Vormittagsstunden setzen', aktion: async () => { const w = await formular('Bedarf für alle Vormittagsstunden', [{ name: 'n', titel: 'Anzahl je Stunde', art: 'zahl', wert: 1 }]); if (w) await aktion('vormittag', { anzahl: Number(w.n) || 0 }); } });
    f.werkzeug({ text: 'Alle entfernen', symbol: 'loeschen', titel: 'Alle Bereitschaften entfernen', aktion: async () => { if (await frage('Alle Bereitschaften aus dem Stundenplan entfernen?', { gefahr: true })) await aktion('alleentfernen'); } });
  }
  const doppelt = f.werkzeug({ text: 'Nur Doppelstunden', symbol: 'untereinander', titel: 'Bereitschaften nur als Doppelstunden verplanen', aktion: () => schreiben && aktion('doppelstunden', { anzahl: daten?.nurDoppelstunden ? 0 : 1 }) });
  f.an('daten', (e) => { if (betrifft(e, 'Stundenplan', 'Einstellungen', 'Stammdaten', 'Unterricht')) laden().catch(() => {}); });
  laden().catch(fehlerMelden);
}

// ---------------------------------------------------------------------------------------------
// Vormerkungen

function vormerkungenFenster(f) {
  const schreiben = darfSchreiben();
  let alle = false;
  const t = listenTabelle(f.inhalt, {
    leerText: 'Keine offenen Vormerkungen',
    zeilenKlasse: (v) => (v.offen ? '' : 'leise'),
    spalten: [
      { titel: 'Nr.', breite: 46, wert: (v) => String(v.id), zelle: (v, td) => {
        setTimeout(() => {
          const zeile = td.parentElement;
          if (!zeile || !schreiben || !v.offen) return;
          zeile.draggable = true;
          zeile.title = 'In einen tagesaktuellen Stundenplan ziehen, um die Vormerkung zu verplanen';
          zeile.addEventListener('dragstart', (e) => { e.dataTransfer.setData(TYP_VORMERKUNG, JSON.stringify({ id: v.id })); e.dataTransfer.effectAllowed = 'move'; });
        });
      } },
      { titel: 'Herkunft', breite: 86, wert: (v) => v.herkunft }, { titel: 'Std.', breite: 40, klasse: 'mitte', wert: (v) => String(v.stunden) },
      { titel: 'offen', breite: 44, klasse: 'mitte', wert: (v) => String(v.offen) }, { titel: 'Fach', breite: 56, wert: (v) => v.fach }, { titel: 'Lehrer', breite: 76, wert: (v) => v.lehrer },
      { titel: 'Klassen', breite: 86, wert: (v) => v.klassen }, { titel: 'Raum', breite: 56, wert: (v) => v.raum }, { titel: 'ursprünglich', breite: 110, wert: (v) => v.urspruenglich },
      { titel: 'gültig', breite: 140, wert: (v) => v.gueltig }, { titel: 'Text', wert: (v) => v.text }, { titel: 'verplant am', breite: 120, wert: (v) => v.verplant },
    ],
  });
  async function laden() { t.setzen(await api.get(vpfad(`/vormerkungen?alle=${alle}`)), true); }
  if (schreiben) {
    f.werkzeug({ text: 'Neu', symbol: 'neu', titel: 'Neue Vormerkung', aktion: async () => {
      const w = await formular('Vormerkung', [{ name: 'fach', titel: 'Fach' }, { name: 'lehrer', titel: 'Lehrer' }, { name: 'klassen', titel: 'Klassen' }, { name: 'raum', titel: 'Raum' }, { name: 'stunden', titel: 'Stunden', art: 'zahl', wert: 1 }, { name: 'text', titel: 'Text' }]);
      if (!w) return;
      await api.post(vpfad('/vormerkungen'), { ...w, stunden: Number(w.stunden) || 1 }, { quelle: f.id });
      await laden();
    } });
    f.werkzeug({ text: 'Am Planungsdatum verplanen', symbol: 'stundenplan', titel: 'Am Planungsdatum verplanen …', aktion: async () => {
      const v = t.aktiv;
      if (!v) return;
      const w = await formular(`Vormerkung am ${datumText(planungsdatum())} verplanen`, [{ name: 'stunde', titel: 'Stunde', art: 'zahl', wert: 1, pflicht: true }]);
      if (!w) return;
      await api.post(vpfad(`/vormerkungen/${v.id}/verplanen`), { datum: planungsdatum(), stunde: Number(w.stunde) }, { quelle: f.id });
      await laden();
    } });
    f.werkzeug({ text: 'Löschen', symbol: 'loeschen', nurSymbol: true, titel: 'Löschen', aktion: async () => {
      const v = t.aktiv;
      if (!v || !(await frage(`Vormerkung ${v.id} löschen?`, { gefahr: true }))) return;
      await api.post(vpfad('/vormerkungen/loeschen'), { ids: [v.id] }, { quelle: f.id });
      await laden();
    } });
  }
  const ak = f.werkzeug({ text: 'Auch verplante', symbol: 'uebersicht', titel: 'Auch vollständig verplante Vormerkungen zeigen', aktion: () => { alle = !alle; ak.classList.toggle('gedrueckt', alle); laden(); } });
  f.fuss.append(h('span', { class: 'leise' }, 'Vormerkung mit der Maus in einen tagesaktuellen Stundenplan ziehen, um sie zu verplanen.'));
  f.an('daten', (e) => { if (betrifft(e, 'Vertretung', 'Stundenplan')) laden().catch(() => {}); });
  laden().catch(fehlerMelden);
}

export const fenster = {
  'vertretung.bereitschaften': { titel: 'Bereitschaften', symbol: 'lehrer', breite: 560, hoehe: 320, einzig: () => 'bereitschaften', erstellen: bereitschaftenFenster },
  'vertretung.bereitschaftsplanung': { titel: 'Bereitschaftsplanung', symbol: 'vertretung', breite: 1000, hoehe: 520, einzig: () => 'bereitschaftsplanung', erstellen: bereitschaftsplanungFenster },
  'vertretung.vormerkungen': { titel: 'Vormerkungen', symbol: 'info', breite: 920, hoehe: 300, einzig: () => 'vormerkungen', erstellen: vormerkungenFenster },
};
