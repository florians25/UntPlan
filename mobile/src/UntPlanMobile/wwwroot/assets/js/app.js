/* =====================================================================
   app.js — UntPlanMobile (Oberfläche der Stundenplan-PWA)
   ---------------------------------------------------------------------
   Übernommen aus der Stundenplan-PWA. Angepasst sind nur die Endpunkte
   (api/mobile, api/login, api/classbook, api/home, api/messages,
   api/accounts statt der PHP-Dateien), die Langnamen und Kategorien aus
   UntPlanWeb, die Fassung und Funktionen, die UntPlanWeb nicht kennt
   (Entwürfe, Anhänge, Notizen für Schüler*innen, Onlineunterricht,
   mehrere Profile).
   ===================================================================== */
(() => {
'use strict';

const S = window.TT_SEED;
const $  = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));

/* =====================================================================
   Datum
   ===================================================================== */
const WD_SHORT = ['Mo.','Di.','Mi.','Do.','Fr.','Sa.','So.'];
const MONTHS_SHORT = ['Jan','Feb','Mär','Apr','Mai','Jun','Jul','Aug','Sept.','Okt','Nov','Dez'];
const MONTHS = ['Januar','Februar','März','April','Mai','Juni','Juli','August','September','Oktober','November','Dezember'];

const iso = d => `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;
const parseISO = s => { const [y,m,d] = s.split('-').map(Number); return new Date(y, m-1, d); };
const addDays = (d, n) => { const x = new Date(d); x.setDate(x.getDate()+n); return x; };

function mondayOf(d){
  const x = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  const wd = (x.getDay() + 6) % 7;          // Mo = 0
  x.setDate(x.getDate() - wd);
  return x;
}
function isoWeek(d){
  const x = new Date(Date.UTC(d.getFullYear(), d.getMonth(), d.getDate()));
  const dayNo = (x.getUTCDay() + 6) % 7;
  x.setUTCDate(x.getUTCDate() - dayNo + 3);
  const first = new Date(Date.UTC(x.getUTCFullYear(), 0, 4));
  const firstDayNo = (first.getUTCDay() + 6) % 7;
  first.setUTCDate(first.getUTCDate() - firstDayNo + 3);
  return 1 + Math.round((x - first) / 604800000);
}
const minutes = t => { const [h,m] = t.split(':').map(Number); return h*60 + m; };
const weekDiff = (a, b) => Math.round((mondayOf(a) - mondayOf(b)) / 604800000);

/* =====================================================================
   Zustand
   ===================================================================== */
const store = {
  get(k, fb){ try { const v = localStorage.getItem('tt.'+k); return v === null ? fb : JSON.parse(v); } catch { return fb; } },
  set(k, v){ try { localStorage.setItem('tt.'+k, JSON.stringify(v)); } catch {} },
  del(k){ try { localStorage.removeItem('tt.'+k); } catch {} }
};

/* Ansicht: Tag, 2 Tage, 3 Tage oder Woche – wie im ⋮-Menü des Originals.
   Gemerkt wird sie auf dem Gerät. */
const TAGE_ANSICHT = { day:1, day2:2, day3:3 };
const tageAnsicht = () => TAGE_ANSICHT[state.view] || 0;

const state = {
  source:   'demo',            // demo | db | scrape
  view:     ['day', 'day2', 'day3', 'week'].includes(store.get('view', 'week')) ? store.get('view', 'week') : 'week',
  day:      null,              // gewählter Tag in der Tagesansicht
  kind:     null,              // vom Server gemeldete Planart
  entities: S.ENTITIES.slice(),
  entity:   null,
  weekStart:iso(mondayOf(new Date())),
  screen:   'plan',
  times:    S.LESSON_TIMES,
  week:     null,
  detail:   null,
  calMonth: null,
  opts: {
    cardTimes:     store.get('cardTimes', false),
    longNames:     store.get('longNames', true),
    showCancelled: store.get('showCancelled', true),
    /* Eigene Abwesenheiten im eigenen Plan als grauer Balken hinter den
       Stunden (⋮-Menü „Abwesenheiten anzeigen“, bleibt gespeichert). */
    showAbsences:  store.get('showAbsences', true)
  }
};

/* Zuletzt gewählter Plan. Der Schlüssel wird gleich noch kontobezogen
   gesetzt, sobald die Serverdaten da sind; ohne sie gilt der allgemeine. */
state.entityKey = 'entity';
const savedEntity = store.get(state.entityKey, null);
state.entity = state.entities.find(e => savedEntity
                 && e.type === savedEntity.type && String(e.id) === String(savedEntity.id))
            || state.entities[0];

/* Vom Server mitgelieferte Livedaten übernehmen */
const BOOT = (window.TT_BOOT && window.TT_BOOT.ok) ? window.TT_BOOT : null;
if (BOOT){
  state.source = 'db';
  if (BOOT.lessonTimes && Object.keys(BOOT.lessonTimes).length) state.times = BOOT.lessonTimes;
  state.kind = BOOT.kind || null;

  /* Über dem Plan steht „Mein Stundenplan", nicht das eigene Kürzel —
     das Kürzel sagt einem über den eigenen Plan nichts. */
  const own = { type:'mine', id:'me',
                label:BOOT.isStudent && BOOT.own?.info ? BOOT.own.info : 'Mein Stundenplan',
                code:BOOT.own?.label || '', info:BOOT.own?.info || '',
                kind:BOOT.own?.kind || '' };
  /* Auch ohne ausdrückliches Kennzeichen: Ein Klassenplan als eigener Plan
     heißt Schülerkonto. Darauf ist Verlass, bevor irgendetwas nachlädt. */
  state.isStudent = !!BOOT.isStudent || (BOOT.own?.kind === 'class');
  /* Auf der Profilseite steht der Mensch — Name und Kürzel getrennt. */
  state.ownName = BOOT.own?.info || '';
  state.ownCode = BOOT.own?.label || '';
  state.ownKind = BOOT.own?.kind || '';
  state.user = BOOT.user || null;
  state.entities = [own, ...(BOOT.entities || [])];
  state.weekStart = BOOT.weekStart || state.weekStart;

  /* Zuletzt gewählter Plan — je Konto getrennt, damit nach einem
     Profilwechsel nicht der Plan des vorigen Kontos erscheint. Ist nichts
     gemerkt, öffnet „Mein Plan". */
  const merkKey = 'entity.' + (BOOT.own?.label || BOOT.own?.info || 'konto');
  const gemerkt = store.get(merkKey, null);
  /* „Mein Plan" ist ein eigener Eintrag, kein Klassen- oder Lehrerplan —
     dahinter steckt die Voreinstellung des Kontos. Wer ihn zuletzt offen
     hatte, bekommt ihn auch wieder als „Mein Plan", nicht als aufgelösten
     Lehrerplan. */
  state.entity = (gemerkt && gemerkt.type === 'mine') ? own
              : (gemerkt && state.entities.find(e =>
                    e.type === gemerkt.type && String(e.id) === String(gemerkt.id)))
              || own;
  state.entityKey = merkKey;

  /* Was der Server als zuletzt gewählt meldet, wird bewusst übergangen:
     Er kennt nur die Sitzung, das Gerät kennt die Wahl. Sonst überschrieb
     sein Wert bei jedem Laden das Gemerkte. Ein Direktlink mit ?entity=…
     greift weiter unten und schlägt beides. */
}

/* Query-String: Datenquelle, Direktlinks und App-Shortcuts */
const qs = new URLSearchParams(location.search);
if (qs.get('db'))  TT_DB.configure({ base: qs.get('db') });        // eigener Pfad zu api/mobile
if (qs.get('demo') === '1') state.source = 'demo-forced';

if (qs.get('entity')){
  const hit = state.entities.find(e => String(e.id) === qs.get('entity'));
  if (hit) state.entity = hit;
}
if (qs.get('week')){
  const n = parseInt(qs.get('week'), 10);
  if (Number.isFinite(n)) state.weekStart = iso(addDays(parseISO(state.weekStart), n * 7));
}

/* =====================================================================
   Rasterberechnung
   ===================================================================== */
const PPM = 1.2704;                 // px je Minute — 45 min = 57,17 px

function buildLayout(times, pre = 0){
  const nos = Object.keys(times).map(Number).sort((a,b) => a-b);
  const t0 = minutes(times[nos[0]].start);
  const rows = [];
  nos.forEach((n, i) => {
    const st = minutes(times[n].start), en = minutes(times[n].end);
    rows.push({ y:(st - t0) * PPM + pre, label:times[n].start, no:n });
    const next = nos[i+1];
    if (!next || minutes(times[next].start) > en)
      rows.push({ y:(en - t0) * PPM + pre, label:times[n].end, no:null });
  });
  const last = nos[nos.length-1];
  return {
    nos, t0, rows, pre,
    total: (minutes(times[last].end) - t0) * PPM + pre,
    top:  n => (minutes(times[n].start) - t0) * PPM + pre,
    height: (n, span) => {
      const end = Math.min(n + span - 1, last);
      return (minutes(times[end].end) - minutes(times[n].start)) * PPM;
    }
  };
}

/* =====================================================================
   Datenbeschaffung
   ===================================================================== */
function weekDates(weekStart){
  const mo = parseISO(weekStart);
  return [0,1,2,3,4].map(i => iso(addDays(mo, i)));
}

function demoWeek(entity, weekStart){
  const off  = String(weekDiff(parseISO(weekStart), new Date()));
  const base = entity.id === 'me' ? S.BASE_ME : entity.id === 'FI428' ? S.BASE_FI428 : S.BASE_GENERIC;
  const ovl  = (S.OVERLAYS[entity.id] || {})[off] || null;
  const dates = weekDates(weekStart);

  const byDate = {};
  dates.forEach((d, i) => {
    const wd = i + 1;
    const src = (ovl && ovl[wd]) || base[wd] || [];
    byDate[d] = src.map(c => ({ ...c, date:d, changed:c.changed || [], blockType:c.blockType || 'normal' }));
  });

  const bands = ((S.ABSENCE_BANDS[entity.id] || {})[off] || [])
    .map(b => ({ ...b, date:dates[b.weekday - 1] }));

  return { byDate, bands, times:null };
}

function cacheKey(entity, weekStart){ return 'cache.' + entity.type + entity.id + weekStart; }

/* Kennung eines Plans und einer geladenen Woche. Beides wird an jeden
   Datensatz gehängt, der im Zustand landet — sonst lässt sich später
   nicht mehr feststellen, zu wem er gehört. Genau daran lagen die
   falschen Kacheln nach einem Planwechsel: Die vorgeladenen Nachbarwochen
   trugen keine Herkunft und wurden beim Wischen einfach übernommen. */
function planKey(entity){ return entity ? entity.type + '#' + entity.id : '?'; }
function weekKey(entity, weekStart){ return planKey(entity) + '#' + weekStart; }

/* Läuft dieselbe Woche schon, wird die laufende Anfrage geteilt statt
   ein zweites Mal geladen — das passiert beim Wischen und Vorladen
   ständig. */
const laufendeLadungen = new Map();

/* --- Gelesene Wochen bleiben im Speicher --------------------------------
   Beim Wischen wurde bisher nach jedem Wechsel alles neu geholt: die
   Woche selbst, sobald sie nicht vorgeladen war, und anschließend beide
   Nachbarn — auch die, aus der man gerade gekommen ist. Zwei, drei
   Wischer hintereinander lösten so ein halbes Dutzend Abfragen aus, und
   der Balken unter der Kopfzeile blinkte dauernd.

   Jetzt liegt jede einmal geladene Woche hier. Wer zurückwischt, sieht
   sie sofort; nachgeladen wird nur, was fehlt oder zu alt ist. Alt heißt
   anderthalb Minuten — Vertretungen ändern sich im Lauf des Tages, aber
   nicht im Sekundentakt. Veraltetes wird still im Hintergrund geholt und
   nur dann neu gezeichnet, wenn sich wirklich etwas geändert hat. */
const wochenSpeicher = new Map();
const WOCHE_FRISCH = 90 * 1000;
const WOCHEN_MAX   = 16;

function wocheMerken(k, daten){
  if (!daten || !daten.byDate) return;
  wochenSpeicher.delete(k);
  wochenSpeicher.set(k, { daten, zeit: Date.now() });
  /* Der älteste Eintrag fällt heraus — der Speicher soll nicht wachsen. */
  while (wochenSpeicher.size > WOCHEN_MAX)
    wochenSpeicher.delete(wochenSpeicher.keys().next().value);
}
function wocheGemerkt(entity, weekStart){
  return wochenSpeicher.get(weekKey(entity, weekStart)) || null;
}
const wocheFrisch = g => !!g && (Date.now() - g.zeit) < WOCHE_FRISCH;
function wochenVergessen(){ wochenSpeicher.clear(); }

function loadWeekShared(entity, weekStart, token){
  const k = weekKey(entity, weekStart);
  const da = laufendeLadungen.get(k);
  if (da) return da;
  const p = Promise.resolve(loadWeek(entity, weekStart, token))
    /* Der Stand des Servers wird festgehalten, bevor das Zeichnen
       Hilfsfelder an die Karten hängt — der stille Abgleich vergleicht
       damit Server gegen Server. */
    .then(w => {
      if (w && w.byDate && w.roh === undefined){
        try { Object.defineProperty(w, 'roh', { value: JSON.stringify(w.byDate), enumerable:false }); } catch (e){}
      }
      wocheMerken(k, w); return w;
    })
    .finally(() => laufendeLadungen.delete(k));
  laufendeLadungen.set(k, p);
  return p;
}

/* Was links und rechts liegt, steht oft schon im Speicher — dann ist der
   Streifen sofort vollständig, ohne eine einzige Abfrage. */
function nachbarnAusSpeicher(){
  const e = state.entity, b = state.weekStart;
  const p = wocheGemerkt(e, iso(addDays(parseISO(b), -7)));
  const n = wocheGemerkt(e, iso(addDays(parseISO(b),  7)));
  state.around    = { prev: p ? p.daten : undefined, next: n ? n.daten : undefined };
  state.aroundKey = weekKey(e, b);
}

function stampDates(cards, date){
  cards.forEach(c => { if (!c.date) c.date = date; nurRaumKeinePruefung(c); });
  return cards;
}

/* Eine Raumänderung ist nie eine Prüfung.
   ---------------------------------------------------------------------
   Gelb mit braunem Rahmen steht ausschließlich für Klausuren und
   Prüfungen. Kommt eine Stunde als „klausur“ herein, deren Art aber nur
   einen Raumwechsel nennt („Raumänderung“, „Raumvertretung“,
   „Raumwechsel“) und nirgends eine Klausur oder Prüfung erwähnt, wird
   sie als grüne Änderung gezeigt — mit dem neuen Raum in der grünen
   Plakette. Gilt für Woche, Tag, Liste, Details und vergangene Stunden,
   weil es hier beim Einlesen geschieht. */
function nurRaumKeinePruefung(c){
  if (!c || c.blockType !== 'klausur') return c;
  const art = String(c.type || '');
  const raumArt = /raum\s*(änderung|aenderung|vertretung|wechsel)/i.test(art);
  const pruefWort = /klausur|prüfung|pruefung|leistungs|arbeit|test/i;
  if (!raumArt || pruefWort.test(art) || pruefWort.test(String(c.info || ''))) return c;
  c.blockType = 'change';
  if (c.room && !(c.changed || []).includes('room')) c.changed = [...(c.changed || []), 'room'];
  return c;
}

/* Aus dem Raum genommene Stunde im Raumplan.
   ---------------------------------------------------------------------
   Wird eine Stunde in einen anderen Raum verlegt oder ihr Raum entfernt,
   gehört sie nicht mehr in den Plan dieses Raums. Der Server lässt sie
   inzwischen weg; ältere Stände (und gespeicherte Wochen) brachten sie
   noch mit — der Raum stand dann nur noch durchgestrichen auf der Kachel. */
function ausRaumGenommen(c, entity){
  if (!c || !entity || entity.type !== 'room' || c.holiday || c.blockType === 'holiday') return false;
  const id = String(entity.id || '').toUpperCase();
  if (!id) return false;
  const jetzt = werteListe(c.room).map(x => x.toUpperCase());
  const vorher = werteListe(c.roomOrig).map(x => x.toUpperCase());
  return !jetzt.includes(id) && vorher.includes(id);
}

/* „Sondereinsatz“ ist eine interne Vertretungsart von UntPlan und steht
   weder auf der Kachel noch in den Details. */
const SONDEREINSATZ = /\(?\bSondereins(?:a|ä)tz(?:e)?\b\)?\s*[:–—-]?\s*/gi;
function ohneSondereinsatz(c){
  if (!c) return c;
  ['info', 'type', 'subject'].forEach(k => {
    if (typeof c[k] === 'string' && /sondereins(a|ä)tz/i.test(c[k]))
      c[k] = c[k].replace(SONDEREINSATZ, ' ').replace(/\s{2,}/g, ' ').replace(/^[\s·,;–-]+|[\s·,;–-]+$/g, '');
  });
  return c;
}

/* Sperre einer Woche aus der Antwort des Servers (oder dem Speicher):
   `gesperrt: true` und `sperrhinweis`. Die Schreibweise wird nicht so eng
   genommen. Ohne Sperre: null. */
function wochenSperre(d){
  if (!d || typeof d !== 'object') return null;
  const an = d.gesperrt === true || d.Gesperrt === true || d.gesperrt === 'true';
  if (!an) return null;
  const h = d.sperrhinweis ?? d.sperrHinweis ?? d.Sperrhinweis ?? '';
  return { hinweis: String(h || '').trim() };
}

let bootUsed = false;
/* Wie loadWeek, aber ohne Meldungen — für das stille Vorladen der
   Nachbarwochen. */
async function loadWeekQuiet(entity, weekStart, token){
  const quiet = toastQuiet;
  toastQuiet = true;
  try { return await loadWeekShared(entity, weekStart, token); }
  finally { toastQuiet = quiet; }
}

let toastQuiet = false;

async function loadWeek(entity, weekStart, token){
  const dates = weekDates(weekStart);
  /* Eine Antwort, die nach einem Planwechsel eintrifft, darf den Zustand
     nicht mehr anfassen — sie beschreibt einen Plan, den niemand mehr
     ansieht. */
  const aktuell = () => token === undefined || token === renderToken;
  const fertig = (byDate, bands, quelle) => {
    /* Auch gespeicherte Wochen von früher durchlaufen die Prüfung. */
    Object.keys(byDate || {}).forEach(d => {
      const l = (byDate[d] || []).filter(c => !ausRaumGenommen(c, entity));
      l.forEach(c => { nurRaumKeinePruefung(c); ohneSondereinsatz(c); });
      byDate[d] = l;
    });
    const w = { byDate, bands: bands || [], key: weekKey(entity, weekStart),
                absences: (quelle && quelle.absences) || {} };
    /* Wochenbegrenzung der Rolle: Der Server liefert die Woche leer und
       meldet sie als gesperrt — gezeigt wird das Schloss mit dem Hinweis. */
    const sp = wochenSperre(quelle);
    if (sp){ w.gesperrt = true; w.sperrhinweis = sp.hinweis; }
    return w;
  };

  /* Die vom Server mitgelieferte Woche braucht keinen zweiten Ladevorgang —
     aber nur, wenn sie auch zu dem Plan gehört, der angezeigt werden soll.
     Wurde ein anderer gemerkt, gehören die mitgelieferten Daten zu einem
     fremden Plan; sie zu übernehmen ließ die Anzeige hängen. */
  const bootPasst = BOOT && (() => {
    const be = BOOT.entity || {};
    if (entity.type === 'mine') return !be.type || be.type === 'mine'
      || String(be.id) === String(BOOT.own?.id ?? '');
    return be.type === entity.type && String(be.id) === String(entity.id);
  })();
  if (BOOT && bootPasst && !bootUsed && weekStart === BOOT.weekStart && entity === state.entity){
    bootUsed = true;
    const byDate = {};
    dates.forEach(x => { byDate[x] = stampDates((BOOT.days && BOOT.days[x]) || [], x); });
    store.set(cacheKey(entity, weekStart), { byDate, bands: BOOT.bands || [], kind: BOOT.kind, absences: BOOT.absences || {},
                                             gesperrt: !!BOOT.gesperrt, sperrhinweis: BOOT.sperrhinweis || '' });
    store.set('lastSync', Date.now());
    return fertig(byDate, BOOT.bands || [], BOOT);
  }

  /* Echte Datenbankdaten */
  if (state.source === 'db'){
    try {
      const d = await TT_DB.week(entity, weekStart);
      if (aktuell()){
        if (d.lessonTimes && Object.keys(d.lessonTimes).length) state.times = d.lessonTimes;
        if (d.kind) state.kind = d.kind;
      }
      const byDate = {};
      dates.forEach(x => { byDate[x] = stampDates((d.days && d.days[x]) || [], x); });
      store.set(cacheKey(entity, weekStart), { byDate, bands: d.bands || [], kind: d.kind, absences: d.absences || {},
                                               gesperrt: !!wochenSperre(d), sperrhinweis: (wochenSperre(d) || {}).hinweis || '' });
      store.set('lastSync', Date.now());
      return fertig(byDate, d.bands || [], d);
    } catch (e){
      if (e && e.auth){ showLogin('Die Sitzung ist abgelaufen. Bitte neu anmelden.'); }

      /* Antwortet der Server mit einem Fehler, ist das kein Offlinebetrieb —
         dann muss die Meldung auch so lauten. */
      const serverMsg = e && e.server ? String(e.message || '').slice(0, 160) : '';
      const c = store.get(cacheKey(entity, weekStart), null);
      if (c){
        if (c.kind && aktuell()) state.kind = c.kind;
        toast(serverMsg ? 'Serverfehler: ' + serverMsg : 'Offline — gespeicherte Woche');
        return fertig(c.byDate, c.bands || [], c);
      }
      toast(serverMsg ? 'Serverfehler: ' + serverMsg : 'Woche konnte nicht geladen werden');
      return fertig(Object.fromEntries(dates.map(x => [x, []])), []);
    }
  }

  if (TT_API.isLive()){
    try {
      const res = await TT_API.fetchWeek(entity, weekStart, dates);
      if (res.times && aktuell()) state.times = res.times;
      store.set('cache.' + entity.type + entity.id + weekStart, res.byDate);
      store.set('lastSync', Date.now());
      return fertig(res.byDate, []);
    } catch (e){
      const cached = store.get('cache.' + entity.type + entity.id + weekStart, null);
      if (cached) { toast('Offline — gespeicherte Daten'); return fertig(cached, []); }
      toast('Daten konnten nicht geladen werden');
      return fertig(Object.fromEntries(dates.map(d => [d, []])), []);
    }
  }
  return demoWeek(entity, weekStart);
}

/* Beim Tippen wird nichts mehr hinterlegt — die Hervorhebung unter dem
   Finger ist überall entfallen. Damit entfällt auch die Buchführung
   darüber, die hier stand: Sie war nur nötig, weil iOS den gedrückten
   Zustand stehen ließ, wenn der Tipp ein Fenster öffnete. */

/* =====================================================================
   Karten-Hilfen
   ===================================================================== */
const viewKind = e => {
  if (state.kind && e.type === 'mine') return state.kind;
  return e.type === 'mine' ? 'teacher' : e.type;
};

/* Zeichen unten rechts auf der Kachel — die Originalgrafiken (info.svg,
   tt-homework-icon.svg) mit ihrem weißen Feld hinter der grauen Scheibe mit
   ausgespartem „i“ bzw. dem Haus, flach (kein Schatten, kein Verlauf). Sie liegen
   einmal in der Symbolsammlung und werden nur per <use> verwiesen — keine
   Kopie des Pfads je Kachel. Farben (hell/dunkel) über fill/color. */
/* Info-Zeichen wie im Original (nachgemessen 116/108): abgerundetes Feld in
   der Farbe der Zeichenfläche, darin eine graue Scheibe mit ausgespartem
   „i“ (Punkt, Schaft mit Fuß). Hell: Feld #f3f5f7, dunkel #313d44. */
const KACHEL_INFO = '<svg class="ticon ticon--info" viewBox="0 0 16 16" aria-hidden="true"><rect class="ticon__feld" width="16" height="16" rx="3.4"/><circle class="ticon__scheibe" cx="8" cy="8" r="6.2"/><circle class="ticon__feld" cx="8" cy="5.05" r="0.98"/><path class="ticon__feld" d="M6.55 6.75H8.72V10.3H9.45V11.35H6.5V10.3H7.25V7.8H6.55Z"/></svg>';
/* Online-Unterricht (online_lesson.svg): Kamera im abgerundeten Quadrat. */
const KACHEL_ONLINE = '<svg class="ticon ticon--online" viewBox="0 0 28 28" aria-hidden="true"><path d="m20 11.011-3 1v-1a1.5 1.5 0 0 0-1.5-1.5h-6a1.5 1.5 0 0 0-1.5 1.5v6a1.5 1.5 0 0 0 1.5 1.5h6a1.5 1.5 0 0 0 1.5-1.5v-1l3 1v-6z"/><path d="M8 24.511a4.5 4.5 0 0 1-4.5-4.5v-12a4.5 4.5 0 0 1 4.5-4.5h12a4.5 4.5 0 0 1 4.5 4.5v12a4.5 4.5 0 0 1-4.5 4.5H8z"/></svg>';
/* Verlegung: Ursprungsstunde mit Pfeil nach rechts (rotes Feld), Zielstunde
   mit Pfeil nach links (grünes Feld). */


/* PLUS-BEGINN (wird von den Tests ausgeführt) */
/* Hinzugekommene Werte (classPlus, teacherPlus, roomPlus): grünes Label mit
   „+“ davor, z. B. „+B070“. */
function plusWert(card, key, text){
  const liste = String((card && card[key + 'Plus']) || '').split(/\s*,\s*/).filter(Boolean).map(x => x.toUpperCase());
  const t = String(text == null ? '' : text);
  return t && liste.includes(t.toUpperCase()) ? '+' + t : t;
}
if (typeof window !== 'undefined') window.UntPlanPlus = { plusWert };
/* PLUS-ENDE */
const KACHEL_VIDEO = '<svg class="ticon ticon--video" viewBox="0 0 24 24" aria-hidden="true"><rect x="2.8" y="6.4" width="12.6" height="11.2" rx="2.4"/><path d="m15.4 10.6 5.8-3.4v9.6l-5.8-3.4z"/></svg>';
/* Schloss (Linien) für gesperrte Wochen. */
const SPERRE_SCHLOSS = '<svg viewBox="0 0 24 24"><rect x="5" y="10.5" width="14" height="9.5" rx="2.2"/><path d="M8.4 10.5V7.8a3.6 3.6 0 0 1 7.2 0v2.7"/><path d="M12 14.2v2.2"/></svg>';
/* SPERRE-BEGINN (wird von den Tests ausgeführt) */
/* Gesperrte Woche oder gesperrter Tag (außerhalb des Plan-Fensters der
   Rolle): genau dieser Satz, darunter kleiner der Hinweis des Servers
   (`sperrhinweis`). Fehlt er oder ist er leer, steht dort derselbe
   Standardsatz wie in der iOS-App und am Rechner. */
const SPERRE_TITEL = 'Es werden hier keine Termine angezeigt.';
const SPERRE_STANDARD = 'Die Anzeige des Stundenplans ist durch Ihren Administrator beschränkt';
function sperrErklaerung(hinweis){
  const t = String(hinweis ?? '').trim();
  return t || SPERRE_STANDARD;
}
window.UntPlanSperre = { titel: SPERRE_TITEL, standard: SPERRE_STANDARD, erklaerung: sperrErklaerung };
/* SPERRE-ENDE */
const KACHEL_HA = '<svg class="ticon ticon--exakt" viewBox="0 0 16 16" aria-hidden="true"><use href="#ic-tt-homework-icon" width="16" height="16"></use></svg>';

/* TRENNER-BEGINN (wird von den Tests ausgeführt) */
/* Trennzeichen zwischen mehreren Werten (Lehrkräfte, Klassen, Räume):
   auf den Stundenkarten ein Komma, in den Stundendetails ein senkrechter
   Strich. */
const KACHEL_TRENNER = ', ';
const DETAIL_TRENNER = ' | ';
const werteListe = v => String(v == null ? '' : v).split(/\s*[,|]\s*/).map(x => x.trim()).filter(Boolean);
const kachelWert = v => werteListe(v).join(KACHEL_TRENNER);
const detailWert = v => werteListe(v).join(DETAIL_TRENNER);
if (typeof window !== 'undefined') window.UntPlanTrenner = { kachelWert, detailWert, KACHEL_TRENNER, DETAIL_TRENNER };
/* TRENNER-ENDE */

/* Anzuzeigender Wert einer Zeile.
   Fehlt der neue Wert, obwohl sich etwas geändert hat, steht der alte
   durchgestrichen da — so sieht eine offene Vertretung aus. */
function lineValue(card, key){
  const now  = kachelWert(card[key]);
  const orig = kachelWert(card[key + 'Orig']);
  const changed = (card.changed || []).includes(key);

  /* Übernommene Stunde im eigenen Plan: Die Stunde selbst bleibt, wie sie
     ist — nur die Plakette wechselt. Ist eine Vertretung eingeteilt, steht
     dort deren Kürzel; ist noch keine da, das eigene, durchgestrichen. */
  /* Ist die eigene Klasse abwesend und der gekoppelte Unterricht läuft
     weiter, hat sich an der Lehrkraft nichts geändert — sie steht schlicht
     da. Durchgestrichen gehört dann die Klasse, nicht das Kürzel. */
  if (card._released && key === 'teacher' && !card._klasseWeg && !card.classAbsent){
    if (now) return { text:now, removed:false, changed:true };
    return { text:orig, removed:true, changed:true };
  }

  if (changed && !now && orig) return { text:orig, removed:true, changed:true };
  return { text:now || orig, removed:false, changed };
}

/* AUFSICHT-BEGINN (wird von den Tests ausgeführt) */
/* Inhalt einer Pausenaufsicht auf der Kachel: immer nur der Ort. Bei einer
   Vertretung (eingesprungen oder abgegeben) steht ganz rechts das grüne
   „…“ — wer vertritt bzw. wer fehlt, steht in den Details (Tippen). Keine
   Kürzel, keine Labels, nichts Durchgestrichenes auf der Kachel. Eine
   entfallene Aufsicht (`_supCancelled`) trägt kein Zeichen: Sie steht im
   Entfall-Stil da (rosa, Ort durchgestrichen), der Anlass in den Details. */
const AUFSICHT_DOTS = '<svg viewBox="0 0 12 12"><circle cx="2.4" cy="6" r="1.05"/><circle cx="6" cy="6" r="1.05"/><circle cx="9.6" cy="6" r="1.05"/></svg>';
function aufsichtInhalt(card){
  const ort = `<span class="card__line">${esc(card.subject || 'Aufsicht')}</span>`;
  const vertreten = !card._supCancelled && (card._supSub || card._supAbsent);
  return ort + (vertreten ? `<span class="sup-menu" aria-hidden="true">${AUFSICHT_DOTS}</span>` : '');
}
/* CSS-Klassen der Aufsichtskachel nach ihrem Zustand. */
function aufsichtKlassen(card){
  if (card._supCancelled) return ['is-cancelled'];
  if (card._supSub) return ['is-sub'];
  if (card._supAbsent) return ['is-absent', 'is-abgegeben'];
  return [];
}
if (typeof window !== 'undefined') window.UntPlanAufsicht = { inhalt: aufsichtInhalt, klassen: aufsichtKlassen };
/* AUFSICHT-ENDE */

/* Die Kürzel einer Kopplung mit ihrem Zustand.

   Ausschließlich für Kopplungen: Die Liste kommt aus der Datenschicht
   (`teacherList`) und entsteht nur dort, wo mehrere Lehrkräfte dieselbe
   Stunde halten. Eine gewöhnliche Vertretung wird hiervon nicht berührt —
   dort bleibt es bei der einen Plakette mit dem einen Kürzel, so wie
   überall sonst. Aus `teacher`/`teacherOrig` wird nichts abgeleitet:
   dabei entstünden bei einer normalen Vertretung zwei Werte nebeneinander.

   Zustände: `ok` hält die Stunde, `absent` fällt aus (durchgestrichen),
   `sub` springt für jemanden ein (Plakette). */
/* Fehlt eine Lehrkraft der Kopplung und vertritt sie niemand, ist ihre
   Vertretung offen: Kürzel durchgestrichen in der grünen Plakette. Ist eine
   Vertretung eingeteilt, steht das alte Kürzel ohne Fläche da. */
function koppOffen(t, toks){
  if (t.state !== 'absent') return false;
  if (toks && toks.some(x => x.state === 'sub' && x.for && x.for.toUpperCase() === t.text.toUpperCase())) return false;
  return t.open !== false;
}

function koppTokens(card){
  const list = Array.isArray(card.teacherList) ? card.teacherList : null;
  if (!list || list.length < 2) return null;
  const toks = list.map(t => ({ text:String(t.code || ''), state:t.state || 'ok',
                                for:String(t.for || ''), open: t.open === undefined ? undefined : !!t.open }))
                   .filter(t => t.text);
  /* Jede Lehrkraft nur einmal. Wer vertreten wird, steht schon als
     durchgestrichenes „für“ neben seiner Vertretung — nicht noch einmal
     als eigenes, abwesendes Kürzel. */
  const gross = x => String(x || '').toUpperCase();
  const vertreten = toks.filter(t => t.state === 'sub' && t.for).map(t => gross(t.for));
  const ohne = toks.filter((t, i) =>
    !(t.state === 'absent' && vertreten.includes(gross(t.text)))
    && toks.findIndex(o => gross(o.text) === gross(t.text) && o.state === t.state) === i);
  /* Eine Kopplung bleibt eine Kopplung, auch wenn danach nur ein Eintrag
     übrig ist, der jemanden vertritt: dann stehen die abwesende und die
     vertretende Lehrkraft nebeneinander. */
  return (ohne.length > 1 || (ohne.length === 1 && ohne[0].state === 'sub' && ohne[0].for)) ? ohne : null;
}

/* Die Klassen einer Kopplung mit ihrem Zustand.

   Zwei Klassen können denselben Unterricht haben — dieselbe Stunde, dasselbe
   Fach, derselbe Raum. Im Lehrer- und Raumplan stehen sie dann gemeinsam auf
   einer Kachel, mit Komma getrennt. Fehlt eine von ihnen (Klassenabwesenheit),
   steht sie durchgestrichen da: Der Unterricht findet statt, nur eben ohne
   sie.

   Im Klassenplan ist der eigene Name schon die Überschrift des Plans. Dort
   steht auf der Kachel nur, wer mit dabei ist — die eigene Klasse einzig
   dann, wenn sie fehlt. Genau das ist die Aussage „läuft ohne uns“. */
function klassTokens(card, kind, alle){
  const list = Array.isArray(card.classList) ? card.classList : null;
  if (!list || list.length < 2) return null;
  let toks = list.map(t => ({ text:String(t.name || ''), state:t.state || 'ok', plus: !!t.plus }))
                 .filter(t => t.text);
  if (!alle && kind === 'class'){
    const me = (state.entity.code || state.entity.label || '').toUpperCase();
    const own = me ? toks.find(t => t.text.toUpperCase() === me) : null;
    if (me && (!own || own.state === 'ok'))
      toks = toks.filter(t => t.text.toUpperCase() !== me);
  }
  return toks.length ? toks : null;
}

/* Zeilenbelegung nach Vorlage:
   Klassenplan   Lehrkraft · Fach · Raum · Klasse
   Lehrerplan    Klasse    · Fach · Raum · Lehrkraft
   Der letzte Wert entfällt, wenn er die betrachtete Entität selbst ist
   und sich nichts geändert hat. */
/* RAUMTOKENS-BEGINN (wird von den Tests ausgeführt) */
/* Mehrere Räume bzw. ein entfernter Raum neben einem verbleibenden: jeder
   Raum für sich, nicht mit Komma zu einem Wert verschmolzen. Hervorgehoben
   wird nur, was neu hinzugekommen bzw. ersetzt ist — der bisherige Raum
   bleibt ein gewöhnliches Label daneben. Ein entfernter Raum (roomRemoved)
   steht durchgestrichen in der grünen Plakette dahinter. null = eine
   gewöhnliche Raumzeile. */
function raumTokens(card, changed){
  const gross = x => x.toUpperCase();
  const jetzt = werteListe(card && card.room);
  const weg = werteListe(card && card.roomRemoved).filter(x => !jetzt.some(y => gross(y) === gross(x)));
  if (jetzt.length < 2 && !(jetzt.length && weg.length)) return null;
  const vorher = werteListe(card.roomOrig).map(gross).filter(x => !weg.some(y => gross(y) === x));
  const neuListe = werteListe(card.roomNew).map(gross);
  const neu = x => !!changed && (neuListe.length ? neuListe.includes(gross(x))
                                 : !weg.length && (!vorher.length || !vorher.includes(gross(x))));
  /* Der neue Raum steht vorn: Auf einer schmalen Kachel kürzt sich die
     Zeile mit „…“, und die grüne Plakette darf dabei nie wegfallen. */
  return jetzt.map(x => ({ text:x, state: neu(x) ? 'sub' : 'ok' }))
    .sort((a, b) => (b.state === 'sub') - (a.state === 'sub'))
    .concat(weg.map(x => ({ text:x, state:'absent', open:true })));
}
if (typeof window !== 'undefined') window.UntPlanRaum = { raumTokens };
/* RAUMTOKENS-ENDE */

function cardLines(card, kind){
  /* Veranstaltung, Klassenabwesenheit, Freisetzung.

     Die Reihenfolge folgt derselben Regel wie beim Unterricht: Oben steht,
     was man im eigenen Plan nicht schon weiß. Im Klassenplan ist das die
     Lehrkraft, im Lehrer- und Raumplan die Klasse. Darunter der Anlass —
     „UG Abschluss Marketing OS“, „Einschulung“ —, und im Lehrerplan
     darunter noch die begleitenden Kolleginnen und Kollegen, falls mehrere
     dabei sind. Vorher stand überall die Klasse oben und der Anlass fehlte
     auf der Kachel ganz. */
  if (card.blockType === 'absence' || card.blockType === 'gs'){
    const anlass = (card.subject || card.info || '').trim();
    const reihe  = (kind === 'class')
      ? [{ key:'teacher', primary:true }]
      : [{ key:'class', primary:true }];
    const zeilen = reihe.map(l => ({ ...l, ...lineValue(card, l.key) })).filter(l => l.text);

    /* Bei einer Kopplung steht in der Lehrerzeile eine Liste. */
    const toksV = koppTokens(card);
    if (kind === 'class' && toksV && zeilen[0]){
      zeilen[0].tokens = toksV;
      zeilen[0].text   = toksV.map(t => t.text).join(', ');
      zeilen[0].changed = false;
    }

    if (anlass) zeilen.push({ key:'subject', text:anlass, primary:!zeilen.length });

    /* Im Lehrer- und Raumplan die weiteren Lehrkräfte darunter — im
       eigenen Plan ohne das eigene Kürzel. */
    if (kind !== 'class'){
      const label = (state.entity.code || state.entity.label || '').toUpperCase();
      const weitere = (toksV ? toksV.map(t => t.text) : [card.teacher || ''])
        .map(t => String(t).trim()).filter(Boolean)
        .filter(t => kind !== 'teacher' || t.toUpperCase() !== label);
      if (weitere.length) zeilen.push({ key:'teacher', text:weitere.join(', ') });
    }
    return zeilen;
  }

  if (card.fullday || card.blockType === 'event') return [
    { key:'subject', primary:true },
    { key:'teacher' },
    { key:'class' }
  ].map(l => ({ ...l, ...lineValue(card, l.key) })).filter(l => l.text);

  const first  = (kind === 'class') ? 'teacher' : 'class';
  const last   = (kind === 'class') ? 'class'   : 'teacher';
  /* Abwesende Klasse im Klassenplan: die durchgestrichene Klasse steht oben,
     damit sie auch auf kleinen Kacheln nicht weggekürzt wird. */
  /* Fehlt in einer Kopplung jemand (abwesend oder vertreten), steht die
     Lehrerzeile vor dem unveränderten Raum — sonst kürzte sie sich auf
     einer einstündigen Kachel als erste weg, obwohl sie die Änderung trägt. */
  const koppGeaendert = kind !== 'class' && !(card.changed || []).includes('room')
    && (koppTokens(card) || []).some(t => (t.state || 'ok') !== 'ok');
  const order  = (card.classAbsent && kind === 'class')
    ? ['class', 'teacher', 'subject', 'room']
    : koppGeaendert ? [first, 'subject', 'teacher', 'room'] : [first, 'subject', 'room', last];

  /* Wessen Plan man ansieht, steht über dem Plan — in jeder Kachel noch
     einmal das eigene Kürzel zu wiederholen, bringt nichts. Das gilt für
     den eigenen Plan genauso wie für den einer anderen Lehrkraft; bisher
     war ausgerechnet der eigene ausgenommen. Geändertes bleibt stehen,
     denn dort sagt das Kürzel etwas. */
  const label = (state.entity.code || state.entity.label || '').toUpperCase();
  const toks  = koppTokens(card);
  const ktoks = klassTokens(card, kind);

  return order.map((key, i) => {
    const l = { key, primary:i === 0, ...lineValue(card, key) };
    /* Bei einer Kopplung steht in dieser Zeile nicht ein Wert, sondern
       eine Liste — jedes Kürzel mit eigenem Zustand. Eine Plakette um
       das Ganze wäre falsch und würde sich obendrein wegkürzen. */
    if (key === 'teacher' && toks){
      l.tokens  = toks;
      l.text    = toks.map(t => t.text).join(', ');
      l.changed = false;
      l.removed = false;
    }
    /* Dasselbe für gekoppelte Klassen: im Lehrerplan oben nebeneinander,
       im Klassenplan als unterste Zeile die Klasse, die mit dabei ist. */
    if (key === 'class' && ktoks){
      l.tokens  = ktoks;
      l.text    = ktoks.map(t => t.text).join(', ');
      l.changed = false;
      l.removed = false;
    }
    /* Mehrere Räume: jeder steht für sich, nicht mit Komma zu einem Wert
       verschmolzen. Hervorgehoben wird nur, was neu hinzugekommen ist —
       der bisherige Raum bleibt ein gewöhnliches Label daneben. */
    if (key === 'room' && !l.removed){
      const rt = raumTokens(card, l.changed);
      if (rt){
        l.tokens  = rt;
        l.text    = rt.map(t => t.text).join(' ');
        l.changed = false;
      }
    }
    return l;
  })
    .filter(l => l.text)
    /* Auf einer Vertretungskachel steht der eigene Wert als Plakette: Er
       sagt, dass ich diese Stunde halte beziehungsweise dass sie in dieser
       Klasse stattfindet. Bei einer Anstelle-Vertretung ändert sich Klasse
       und Fach, die Lehrkraft bleibt ich — `changed` enthält dann kein
       `teacher`, und die Zeile wäre sonst weggefallen. */
    /* Nicht bei einer reinen Raumänderung: Dort bleibt die Lehrkraft
       dieselbe, und eine Plakette mit dem eigenen Kürzel behauptete eine
       Vertretung, die es nicht gibt. */
    /* Und nicht, wenn allein eine der gekoppelten Klassen fehlt: An der
       Lehrkraft ändert sich dabei nichts, sie hält die Stunde wie geplant.
       Eine Plakette auf dem eigenen Kürzel sagte dort das Gegenteil. Hat
       die Lehrkraft wirklich gewechselt, steht `changed` ohnehin schon —
       diese Regel fügt nur hinzu, sie nimmt nichts weg. */
    .map(l => (label && l.key === last && !l.tokens && card.blockType === 'change'
               && kind === 'teacher' && !card.classAbsent
               && !(ktoks && ktoks.some(t => (t.state || 'ok') === 'absent')
                    && !(card.changed || []).includes('teacher'))
               && !((card.changed || []).length
                    && (card.changed || []).every(k => k === 'room'))
               && l.text.toUpperCase() === label)
              /* `removed` bleibt erhalten: Bei einer offenen Vertretung ist
                 die Plakette durchgestrichen — sie sagt, dass die Lehrkraft
                 wegfällt und noch niemand eingeteilt ist. Das galt auch für
                 die abwesende Lehrkraft selbst und ging hier verloren. */
              ? { ...l, changed:true } : l)
    /* Sonst fällt die letzte Zeile weg, wenn sie nur die betrachtete
       Entität wiederholt — im eigenen Plan also das eigene Kürzel. */
    .filter(l => !(label && l.key === last && !l.changed && !l.tokens
                   && l.text.toUpperCase() === label))
    /* Im Raumplan gilt dasselbe für den Raum: Welcher Raum gemeint ist,
       steht über dem Plan. Auf jeder Kachel noch einmal denselben Namen zu
       wiederholen, füllt nur eine Zeile. Hat sich der Raum geändert oder
       ist er weggefallen, bleibt er stehen — dort sagt er etwas. */
    .filter(l => !(kind === 'room' && l.key === 'room' && !l.changed && !l.removed
                   && label && l.text.toUpperCase() === label));
}

/* Zeilenabstand der Kachel wie in app.css. */
const KARTE_SCHMAL = window.matchMedia ? window.matchMedia('(max-width:699px)') : null;
/* Sprint 26: Kartentexte etwas enger – 13,5 px am iPhone, sonst 13,8 px (wie iOS). */
const karteZeile = () => (KARTE_SCHMAL && KARTE_SCHMAL.matches) ? 13.5 : 13.8;

function cardEndDate(card){
  const t = state.times[Math.min(card.lesson + card.span - 1, 12)] || state.times[card.lesson];
  if (!t) return null;
  const d = parseISO(card.date);
  const [h, m] = t.end.split(':').map(Number);
  d.setHours(h, m, 0, 0);
  return d;
}

/* Karten, die sich zeitlich überschneiden, innerhalb einer Liste
   durchnummerieren. Karten ohne Überschneidung bekommen die volle Breite. */
function groupOverlaps(list){
  const groups = [];
  list.slice()
    .sort((a,b) => a.lesson - b.lesson || b.span - a.span)
    .forEach(c => {
      const g = groups.find(gr => gr.some(o =>
        c.lesson < o.lesson + o.span && o.lesson < c.lesson + c.span));
      if (g) g.push(c); else groups.push([c]);
    });
  groups.forEach(g => g.forEach((c, i) => { c._i = i; c._n = g.length; }));
}

/* ABWESENHEITSFELD-BEGINN (wird von den Tests ausgeführt) */
/* Lage eines Abwesenheitsbalkens (b: { from, to, text }) in der Tagesspalte
   und die größte Lücke darin, in der keine Karte liegt (belegt: [[oben,
   unten], …] in px; y: Uhrzeit → px). Text nur, wenn die Lücke mindestens
   18 px hoch ist. */
function abwesenheitsFeld(b, belegt, y){
  const oben = y(b.from) + 1, unten = Math.max(oben + 8, y(b.to) - 1);
  let luecke = [oben, oben], pos = oben;
  belegt.filter(([o, u]) => u > oben && o < unten).sort((a, c) => a[0] - c[0]).forEach(([o, u]) => {
    if (o - pos > luecke[1] - luecke[0]) luecke = [pos, o];
    pos = Math.max(pos, u);
  });
  if (unten - pos > luecke[1] - luecke[0]) luecke = [pos, unten];
  const textHoehe = luecke[1] - luecke[0];
  return { oben, hoehe: unten - oben, textOben: luecke[0] - oben, textHoehe, mitText: textHoehe >= 18 };
}
if (typeof window !== 'undefined') window.UntPlanAbwesenheit = { abwesenheitsFeld };
/* ABWESENHEITSFELD-ENDE */

/* PARALLEL-BEGINN (wird von den Tests ausgeführt) */
/* Läuft die Stunde ohne mich — abgegeben, offen vertreten, Kopplung ohne
   mich, Klasse abwesend? Dann zeigt der Plan sie mit Eckklammern statt als
   grüne Änderung. Gemeinsam genutzt von der Darstellung und der Rangfolge
   paralleler Karten (grüne Karten stehen vorne). */
function freigegeben(card, kind){
  const isStandby = card.blockType === 'standby';
  const myCode = (state.entity.code || state.entity.label || '').toUpperCase();
  const handedOver = kind !== 'class'
    && (card.changed || []).includes('teacher')
    && (card.teacherOrig || '').toUpperCase() === myCode
    && (card.teacher || '').toUpperCase() !== myCode;
  /* Der verlegte Block am Zielort bleibt eine grüne Änderung, auch wenn
     die Stunde damit formal aus dem eigenen Plan wandert. */
  const isMovedTarget = card._isNewPlace
    || (/verleg/i.test(card.type || '') && card.blockType === 'change');
  /* Fehle ich in einer Kopplung, die die übrigen weiterhalten, läuft
     die Stunde ohne mich — dieselbe Darstellung wie bei jeder anderen
     abgegebenen Stunde: Eckklammern, kein Entfall. Auch dann, wenn die
     Zeile für mich als Entfall hereinkommt. */
  const koppL = Array.isArray(card.teacherList) ? card.teacherList : null;
  const koppOhneMich = !!koppL && kind !== 'class' && !!myCode
    && koppL.some(t => String(t.code || '').toUpperCase() === myCode
                    && (t.state || 'ok') === 'absent')
    && koppL.some(t => (t.state || 'ok') !== 'absent');
  /* Eine Aufsicht wird nicht „abgegeben“ — sie findet statt oder nicht.
     Die Eckklammern-Darstellung würde ihr die eigene Kennzeichnung
     nehmen: Balkenfarbe und Menüzeichen hängen an `card--supervision`,
     und die wurde für freigegebene Karten nicht gesetzt. */
  const isSup = card.blockType === 'supervision' || !!card._supervision;
  /* Ist die eigene Klasse abwesend, der Unterricht läuft aber mit der
     gekoppelten Klasse weiter, ist das kein Entfall: Die Stunde findet
     statt, nur ohne uns. Dieselbe Darstellung wie bei einer Lehrkraft,
     deren Kopplung ohne sie weiterläuft. */
  const klasseWeg = kind === 'class' && !!card._klasseWeg;
  /* Ist die Klasse abwesend (Freisetzung), läuft die Stunde im Lehrer- und
     Raumplan wie eine abgegebene Stunde (Eckklammern, Klasse durchgestrichen
     in der Plakette); im Klassenplan bleibt sie eine grüne Änderung. Nur ein
     echter Entfall ist rot. */
  /* `teacherAbsent` liefert der Server, wenn die Stunde im eigenen Plan
     ohne die (abwesende) Lehrkraft läuft – gleiche Darstellung. */
  /* Im Raumplan ist eine abwesende Lehrkraft (offen oder vertreten) eine
     grüne Änderung wie im Klassenplan – keine Eckklammern, kein Entfall. */
  const lehrerWegHier = (card.openSub || card.teacherAbsent) && kind !== 'room';
  return (((lehrerWegHier || handedOver || koppOhneMich || card.classAbsent) && kind !== 'class')
                    || klasseWeg)
                && !isStandby && !isSup && !isMovedTarget;
}

/* Grün gezeigte Karte: Änderung, Vertretung, Raumänderung, Zusatzstunde. */
function istGrueneKarte(card, kind){
  return card.blockType === 'change' && !freigegeben(card, kind);
}

/* Überlappende Karten nebeneinander legen.
   Nach Uhrzeit gesetzte Einträge — Bereitschaften, Aufsichten — bleiben außen
   vor: sie liegen meist in einer Pause und dürfen nicht hinter einer
   Zählkarte verschwinden. */
function assignParallel(cards){
  /* Aufsichten und Bereitschaften gehören nie in eine Parallelgruppe.
     Erkannt werden sie an ihrer Kennzeichnung, nicht allein an Anfangs-
     und Endzeit: Fehlt die Uhrzeit — etwa weil sich das Pausenfenster
     nicht bestimmen ließ —, landeten sie im Unterricht, teilten sich die
     Spalte mit einer Veranstaltung und verschwanden auf schmalen Spalten
     in der Zählkarte. Sichtbar waren sie dann nur noch über die
     Tagesansicht. */
  /* Nur Aufsichten. Eine Bereitschaft belegt eine ganze Stunde und
     verhält sich damit wie Unterricht: Sie teilt die Spalte, sie kann
     verdrängt werden und gehört dann hinter die Zählkarte. */
  const inPause = c => !!(c._supervision || c._pause || c.blockType === 'supervision');
  const timed = cards.filter(c => (c.from && c.to) || inPause(c));
  /* Untereinander teilen sich Aufsichten und Bereitschaften die Spalte wie
     alle anderen auch — zwei Aufsichten in derselben Pause standen sonst
     beide auf voller Breite übereinander. Mit dem Unterricht mischen sie
     sich weiterhin nicht. */
  timed.forEach(c => { c._i = 0; c._n = 1; });
  /* Aufsichten und Bereitschaften liegen in einer Pause und stehen über
     die volle Breite. Sie teilen die Spalte mit niemandem — weder mit dem
     Unterricht noch untereinander: Sonst wurde aus einem Feld, einer Stunde
     und einer Aufsichtsvertretung eine Dreierteilung, obwohl die Aufsicht
     zeitlich gar nicht im Unterricht liegt. */

  cards = cards.filter(c => !((c.from && c.to) || inPause(c)));
  /* Innerhalb einer Parallelgruppe steht vorne, was tatsächlich stattfindet.
     Danach kommt, was ohne mich läuft, zuletzt der Entfall. Die verdrängten
     Karten erscheinen als Zählkarte — auf 66 px Spaltenbreite bliebe von
     zwei halben Karten kein lesbarer Text. */
  const me = (state.entity.code || state.entity.label || '').toUpperCase();
  const handedAway = c => (c.changed || []).includes('teacher')
    && (c.teacherOrig || '').toUpperCase() === me
    && (c.teacher || '').toUpperCase() !== me;
  const rank = c => {
    /* Eine Kopplung, in der niemand mehr die Stunde hält, ist ein Entfall —
       auch wenn sie noch als Änderung geliefert wird. Daneben steht dann
       eine Stunde, die wirklich stattfindet, und die gehört nach vorn. */
    const kopp = Array.isArray(c.teacherList) ? c.teacherList : null;
    if (kopp && kopp.length > 1 && !kopp.some(t => (t.state || 'ok') !== 'absent')) return 4;
    if (c.blockType === 'cancelled') return 4;
    /* Läuft die Stunde ohne die eigene Klasse weiter, steht sie wie jede
       andere abgegebene Stunde hinter dem, was wirklich stattfindet. */
    if (c._klasseWeg) return 3;
    /* Abwesende Klasse: im Lehrer- und Raumplan wie eine abgegebene Stunde. */
    if (c.classAbsent && viewKind(state.entity) !== 'class') return 3;
    if (c.openSub || handedAway(c)) return 3;
    if (c.overlay) return 2;          /* AK, GS, Veranstaltung */
    /* Eine Vertretung geht der unveränderten Stunde vor. Wird es eng und
       eine Karte weicht dem Zähler, soll die stehen bleiben, an der sich
       etwas geändert hat — die andere steht ohnehin so im Plan. */
    if (c.blockType === 'change' || (c.changed || []).length) return 0;
    return 1;                          /* findet unverändert statt */
  };
  /* Grüne Karten (Änderung, Vertretung, Raumänderung, Zusatzstunde) haben
     bei parallelen Blöcken immer Vorrang: Sie stehen ganz links, und wenn
     wegen Platzmangel nur ein Block mit Zählkarte bleibt, ist es die grüne.
     Danach gilt die übrige Rangfolge wie bisher. Eine Kopplung, in der
     niemand mehr hält, bleibt ein Entfall. */
  const kind = viewKind(state.entity);
  const vorrang = c => rank(c) < 4 && istGrueneKarte(c, kind) ? -1 : rank(c);
  /* Der Rang entscheidet vor der Anfangsstunde. Ein vierstündiger Entfall
     beginnt früher als die grüne Stunde, die in der 3. und 4. hineingezogen
     wurde — nach Startzeit sortiert stünde er deshalb vorn und die Stunde,
     die tatsächlich stattfindet, dahinter. Vorne steht, was stattfindet. */
  const sorted = cards.slice().sort((a,b) =>
    vorrang(a) - vorrang(b) || a.lesson - b.lesson || b.span - a.span);
  /* Zusammenhängende Ketten bilden: alles, was sich über Zwischenschritte
     berührt, gehört in dieselbe Gruppe.

     Eine Karte kann dabei mehrere bestehende Gruppen verbinden, und dann
     müssen die zu **einer** werden. Vorher landete sie nur in der ersten,
     die sie berührte — die übrigen blieben getrennt und wussten nichts von
     ihr.

     Genau daran ist die Vorziehung gescheitert: Vier einzelne vorgezogene
     Stunden berühren einander nicht, jede war also zunächst ihre eigene
     Gruppe. Der Entfall darüber kommt wegen seines Rangs zuletzt an die
     Reihe und schloss sich nur der ersten an. Nur diese erste Stunde wusste
     deshalb, dass hinter ihr etwas liegt, und machte für den Zählstreifen
     Platz; die anderen drei hielten sich für allein in der Spalte und
     liefen in voller Breite unter dem Streifen durch.

     Beim Verschmelzen bleibt die Rangfolge erhalten — sie entscheidet
     gleich darauf, was vorne in die Spalte kommt. */
  const rang = new Map(sorted.map((c, i) => [c, i]));
  const groups = [];
  sorted.forEach(c => {
    const treffer = groups.filter(gr => gr.some(o =>
      c.lesson < o.lesson + o.span && o.lesson < c.lesson + c.span));
    if (!treffer.length){ groups.push([c]); return; }
    const g = treffer[0];
    g.push(c);
    treffer.slice(1).forEach(w => {
      w.forEach(o => g.push(o));
      groups.splice(groups.indexOf(w), 1);
    });
    g.sort((a, b) => rang.get(a) - rang.get(b));
  });

  /* Innerhalb einer Kette in Spalten packen: jede Karte kommt in die erste
     Spalte, deren letzte Karte vorher zu Ende ist. Karten, die sich gar
     nicht überschneiden, teilen sich damit eine Spalte, statt die Gruppe
     unnötig schmal zu machen und gegeneinander zu versetzen. */
  const ueber = (a, b) => a.lesson < b.lesson + b.span && b.lesson < a.lesson + a.span;
  const gruen = new Set(sorted.filter(c => vorrang(c) < 0));
  groups.forEach(g => {
    const cols = [];
    g.forEach(c => {
      /* Was nicht grün ist, steht erst rechts neben jeder gleichzeitigen
         grünen Karte — nie links davon, auch wenn dort eine Spalte frei ist. */
      let ab = 0;
      if (!gruen.has(c)) cols.forEach((col, i) => { if (col.some(o => gruen.has(o) && ueber(o, c))) ab = i + 1; });
      let k = cols.findIndex((col, i) => i >= ab && col.every(o => !ueber(o, c)));
      if (k < 0){ cols.push([c]); k = cols.length - 1; }
      else cols[k].push(c);
      c._i = k;
    });
    g.forEach(c => { c._n = cols.length; });
  });
  return sorted.concat(timed);
}
if (typeof window !== 'undefined') window.UntPlanParallel = { assignParallel, istGrueneKarte };
/* PARALLEL-ENDE */

/* =====================================================================
   Rendering: Wochenkopf
   ===================================================================== */
function renderWeekHead(){
  const dates = weekDates(state.weekStart);
  const mo = parseISO(state.weekStart);
  /* In der Tagesansicht gelten KW und Monat für den gezeigten Tag. */
  const bezug = (tageAnsicht() && state.day) ? parseISO(state.day) : mo;
  $('#kwNo').textContent = 'KW ' + isoWeek(bezug);
  $('#kwMonth').textContent = MONTHS_SHORT[bezug.getMonth()];

  const today = iso(new Date());
  /* In der Tagesansicht steht im Kopf nur der gewählte Tag, mittig. */
  if (tageAnsicht()){
    const n = tageAnsicht();
    const trio = tagesStreifen(state.day, n);
    $('#weekDays').style.display = '';
    $('#weekDays').style.gridTemplateColumns = '';
    /* Dieselbe Breite wie der Rasterstreifen: Der lässt in der Tagesansicht
       rechts drei Punkte frei, damit die Karte nicht an der Kante klebt.
       Ohne diesen Abzug wäre eine Spalte hier drei Punkte breiter als
       unten — die Datumsleiste liefe beim Wischen aus dem Takt. */
    $('#weekDays').innerHTML = `<div class="weekhead__strip" id="headStrip"
        style="grid-template-columns:repeat(${trio.length}, ${100 / n}%); width:calc(100% - ${n === 1 ? 3 : 0}px)"></div>`;
    $('#headStrip').innerHTML = trio.map(d => {
      const dd = parseISO(d);
      /* Derselbe Kopf wie in der Woche: „Fr.“ über „16“ — nur eben eine
         einzige, breite Spalte. KW und Monat stehen links wie dort. */
      return `<button class="dayhead ${n === 1 ? 'dayhead--day' : ''} ${d === today ? 'is-today' : ''}" data-day="${d}">
         <span class="dayhead__name">${WD_SHORT[(dd.getDay()+6)%7]}</span>
         <span class="dayhead__num">${dd.getDate()}</span>
       </button>`;
    }).join('');
    stripRuhe($('#headStrip'));
    /* Tippen auf einen Tag: in der Tagesansicht zurück zur Woche, in der
       2- oder 3-Tage-Ansicht auf genau diesen Tag. */
    $$('#weekDays .dayhead').forEach(b => b.addEventListener('click', () => {
      if (n === 1){ if (b.dataset.day === state.day) setView('week'); }
      else setView('day', b.dataset.day);
    }));
    return;
  }
  /* Drei Wochen nebeneinander: die vorige, die aktuelle, die nächste.
     Beim Wischen wandert der ganze Streifen, wie in einer nativen App. */
  const strip = [-7, 0, 7].flatMap(off =>
    weekDates(iso(addDays(mo, off))));

  $('#weekDays').style.display = '';
  $('#weekDays').innerHTML = `<div class="weekhead__strip" id="headStrip"
      style="grid-template-columns:repeat(${strip.length}, ${100 / 5}%)"></div>`;
  $('#headStrip').innerHTML = strip.map((d, i) => {
    const dd = parseISO(d);
    const sel = state.view === 'day' && d === state.day;
    return `<button class="dayhead ${d === today ? 'is-today' : ''} ${sel ? 'is-picked' : ''}" data-day="${d}">
      <span class="dayhead__name">${WD_SHORT[i % 5]}</span>
      <span class="dayhead__num">${dd.getDate()}</span>
    </button>`;
  }).join('');
  stripRuhe($('#headStrip'));

  $$('#weekDays .dayhead').forEach(b => b.addEventListener('click', () => {
    const d = b.dataset.day;
    /* Der Kopf zeigt auch Tage der Nachbarwochen — dann wandert die Woche mit. */
    const mon = iso(mondayOf(parseISO(d)));
    if (mon !== state.weekStart){
      state.weekStart = mon;
      state.around = {};
      setView('day', d);
      refresh();
      return;
    }
    if (state.view === 'day' && state.day === d) setView('week');
    else setView('day', d);
  }));
}

/* =====================================================================
   Rendering: Raster
   ===================================================================== */
/* Ruhelage beider Streifen: genau eine Rasterbreite nach links. Beide
   bekommen denselben Pixelwert — die Datumsleiste ist in der Tagesansicht
   drei Punkte breiter als das Raster, eine Prozentangabe liefe dort
   auseinander. */
function stripRuhe(el){
  if (!el) return;
  const raster = document.getElementById('colStrip');
  const w = (raster && raster.clientWidth) || el.clientWidth || 1;
  el.classList.add('is-dragging');            /* ohne Übergang umsetzen */
  el.style.transform = `translate3d(${-w}px, 0, 0)`;
  void el.offsetWidth;                        /* Reflow erzwingen */
  el.classList.remove('is-dragging');
}

/* --- Raster abgleichen statt neu aufbauen ------------------------------
   Das Raster wird weiterhin als eine Zeichenkette gebaut — das ist schnell
   und übersichtlich. Eingesetzt wird es aber nicht mehr blind per
   innerHTML: Jeder Knoten merkt sich, aus welchem Quelltext er entstand
   (`_src`). Ist ein neuer Knoten Zeichen für Zeichen gleich, bleibt der
   alte stehen — samt seiner Vermessung (Kürzungen). Die Behälter (Raster,
   Rahmen, Streifen, Tagesspalten) werden dabei Ebene für Ebene abgeglichen,
   alles darunter (Kachel, Zeitzeile, Jetzt-Linie) als Ganzes ersetzt.
   Rückgabe: false = nichts geändert, true = alles neu, sonst die Liste
   der neu eingesetzten Knoten. */
const RASTER_BEHAELTER = new Set(['grid', 'colclip', 'colstrip', 'daycol']);
let rasterQuelle = '', rasterBreite = -1;

function rasterEinsetzen(pane, html){
  const breite = pane.clientWidth;
  if (html === rasterQuelle && breite === rasterBreite && pane.firstElementChild) return false;
  const tpl = document.createElement('template');
  tpl.innerHTML = html;
  const neuRoot = tpl.content.firstElementChild;
  const altRoot = pane.firstElementChild;
  const ganz = !altRoot || !neuRoot || breite !== rasterBreite
            || !altRoot.classList.contains(neuRoot.className) || pane.children.length !== 1;
  rasterQuelle = html; rasterBreite = breite;
  if (ganz){
    /* Quelltext jedes Blattknotens merken, bevor vermessen und gekürzt wird. */
    merkeBlaetter(neuRoot);
    pane.replaceChildren(tpl.content);
    return true;
  }
  const neue = [];
  rasterAbgleich(altRoot, neuRoot, neue);
  return neue;
}

/* Quelltext der Blattknoten (und Inhalt der Tagesspalten) merken, bevor
   vermessen und gekürzt wird. */
function merkeBlaetter(el){
  [...el.children].forEach(k => {
    if (RASTER_BEHAELTER.has(k.className)){
      if (k.className === 'daycol') k._inner = k.innerHTML;
      merkeBlaetter(k);
    } else k._src = k.outerHTML;
  });
}

function rasterAbgleich(alt, neu, neue){
  /* Eigenschaften des Behälters (Höhe, Lage) übernehmen. */
  for (const a of [...alt.attributes]) if (!neu.hasAttribute(a.name)) alt.removeAttribute(a.name);
  for (const a of neu.attributes) if (alt.getAttribute(a.name) !== a.value) alt.setAttribute(a.name, a.value);

  const alte = [...alt.children], neuK = [...neu.children];
  const pool = new Map();
  alte.forEach(k => {
    if (!k._src) return;
    const l = pool.get(k._src); if (l) l.push(k); else pool.set(k._src, [k]);
  });
  /* Tagesspalten zusätzlich nach Inhalt: Beim Wochenwechsel wandert ein
     Tag nur an eine andere Stelle des Streifens — seine Kacheln bleiben
     gleich, nur die Lage der Spalte ändert sich. */
  const innen = new Map();
  alte.forEach(k => {
    if (k._inner == null) return;
    const l = innen.get(k._inner); if (l) l.push(k); else innen.set(k._inner, [k]);
  });
  const benutzt = new Set();
  const nimm = (liste) => {
    while (liste && liste.length){
      const k = liste.shift();
      if (!benutzt.has(k)){ benutzt.add(k); return k; }
    }
    return null;
  };
  /* Erst alle genauen Treffer (gleicher Quelltext bzw. gleicher Inhalt einer
     Tagesspalte) vergeben, dann den Rest nach Lage. In einem Durchgang nähme
     beim Zurückblättern die erste neue Spalte die alte an derselben Stelle
     weg — genau die, die fünf Spalten weiter unverändert gebraucht wird. */
  const quellen = neuK.map(n => n.outerHTML);
  const ziel = neuK.map((n, i) => {
    const k = nimm(pool.get(quellen[i]));
    if (k) return k;
    if (n.className === 'daycol'){
      const g = nimm(innen.get(n.innerHTML));
      if (g){
        for (const at of n.attributes) if (g.getAttribute(at.name) !== at.value) g.setAttribute(at.name, at.value);
        g._src = quellen[i];
        return g;
      }
    }
    return null;
  });
  ziel.forEach((k, i) => {
    if (k) return;
    const n = neuK[i], src = quellen[i];
    /* Verglichen wird die Grundklasse — Zustände wie `is-dragging` am
       Streifen hängen nur vorübergehend daran. */
    const a = alte[i];
    if (a && !benutzt.has(a) && a.tagName === n.tagName
        && RASTER_BEHAELTER.has(n.className) && a.classList.contains(n.className)){
      benutzt.add(a);
      if (n.className === 'daycol') a._inner = n.innerHTML;
      rasterAbgleich(a, n, neue);
      a._src = src;
      ziel[i] = a;
      return;
    }
    n._src = src;
    if (n.className === 'daycol'){ n._inner = n.innerHTML; merkeBlaetter(n); }
    neue.push(n);
    ziel[i] = n;
  });
  alte.forEach(k => { if (!benutzt.has(k)) k.remove(); });
  /* Reihenfolge herstellen — bewegt wird nur, was nicht schon passt. */
  ziel.forEach((k, i) => {
    const jetzt = alt.children[i];
    if (jetzt !== k) alt.insertBefore(k, jetzt || null);
  });
}

/* Breite der Zeitspalte: nur einmal je Fensterbreite aus dem Stil lesen
   (getComputedStyle erzwingt sonst bei jedem Zeichnen eine Stilberechnung). */
let zeitspalteMerk = { w:-1, px:45 };
function zeitspalteBreite(){
  const w = window.innerWidth;
  if (zeitspalteMerk.w !== w){
    zeitspalteMerk = { w, px: parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--gutter-w')) || 45 };
  }
  return zeitspalteMerk.px;
}

function renderGrid(){
  const nTage = tageAnsicht();
  /* „isDay“ ist die Tagesansicht im engeren Sinn (eine Spalte, Feld links
     und Stunden daneben). 2 und 3 Tage verhalten sich wie die Woche, nur
     mit breiteren Spalten. */
  const isDay = nTage === 1;
  const mo0 = parseISO(state.weekStart);
  /* Auch die Tagesansichten sind ein Streifen: davor, die gezeigten Tage,
     danach – je so viele Tage, wie die Ansicht zeigt. */
  const dates = nTage
    ? tagesStreifen(state.day, nTage)
    : [-7, 0, 7].flatMap(off => weekDates(iso(addDays(mo0, off))));
  const pane = $('#gridPane');

  /* Karten aller drei Wochen an einem Ort. */
  const around = state.around || {};
  const byDate = Object.assign({},
    (around.prev || {}).byDate, state.week.byDate, (around.next || {}).byDate);
  /* Auch die Nachbarwochen sind anklickbar — der Streifen zeigt sie ja. */
  state.allByDate = byDate;
  /* Abwesenheitsbalken ebenso aus allen drei Wochen. */
  const absences = Object.assign({},
    (around.prev || {}).absences, state.week.absences, (around.next || {}).absences);

  /* Aufsichten vor der ersten Stunde (und ganztägige Termine als Pille)
     stehen direkt über der ersten Stunde – nicht oben am Rand mit leerem
     Streifen dazwischen. Platz dafür gibt es nur, wenn im gezeigten
     Streifen (alle drei Wochen bzw. Tage) wirklich eine solche Pille liegt;
     sonst beginnt das Raster wie im Original 15 px unter der Tagesleiste.
     Wie in der iOS-App: mit Pille 34 px bis zur ersten Linie, ohne 15 px. */
  /* PRE_LUFT: Abstand der Pille zur ersten Stunde. Mit 2 px wirkte sie
     abgesetzt; jetzt liegt sie so dicht davor wie die Pausenaufsichten
     zwischen zwei Stunden (gut 1,5 px sichtbare Luft). */
  const PRE_PILLE = 23, PRE_LUFT = 0.5;
  const hatVorErste = dates.some(d => (byDate[d] || []).some(c => c._pausePre
    && (state.opts.showCancelled || c.blockType !== 'cancelled')));
  const preInset = hatVorErste ? PRE_PILLE + PRE_LUFT + 1 : 0;
  if (pane) pane.classList.toggle('has-pre', hatVorErste);

  const L = buildLayout(state.times, preInset);
  const now = new Date();
  const todayISO = iso(now);

  const atTime = t => { const [h,m] = String(t).split(':').map(Number); return ((h*60+m) - L.t0) * PPM + L.pre; };
  const cols  = nTage || 5;             /* Spalten je sichtbarem Abschnitt */

  /* Wie viele Kacheln passen nebeneinander in eine Spalte, ohne dass der
     Text unlesbar wird? Auf dem Telefon ist das eine, auf breiten Geräten
     wie dem iPad sind es mehrere. Erst wenn es nicht mehr reicht, tritt die
     Zählkarte an die Stelle der verdrängten Kacheln. Sprint 27: Nebeneinander
     nur, solange jeder Block mindestens 40 px breit bleibt – sonst vorne der
     erste Block und daneben die Zählkarte (Tippen öffnet den Tag mit allen
     parallelen Stunden). Das gilt für Kopplungen, Vertretungen und Entfälle
     gleichermaßen; die iPhone-Woche zeigt damit meist einen Block mit
     Zählkarte, Tagesansicht und iPad stehen nebeneinander. */
  const MIN_CARD = 40;
  const gutterPx = zeitspalteBreite();
  const colPx = Math.max(1, (($('#gridScroll').clientWidth || 402) - gutterPx) / cols);
  /* Breite Wochenspalte: Platz für eine halbe Kachel neben einem Feld. */
  const nebenFeld = !isDay && colPx >= 180;
  const weekPx = colPx * cols;
  const fitSide = Math.max(1, Math.floor(colPx / MIN_CARD));

  const grenzeFuer = () => fitSide;

  /* Nebeneinanderliegende Kacheln: die Luft dazwischen wird von der Breite
     abgezogen, nicht angehängt. Dadurch endet die letzte Kachel genau dort,
     wo auch eine volle Kachel endet — rechts bündig. */
  const lane = (n, i, from, to, gap) => {
    const span = `${to - from}% - ${(n - 1) * gap}px`;
    return `left:calc(${from}% + ${i} * ((${span}) / ${n} + ${gap}px));`
         + `width:calc((${span}) / ${n});`;
  };
  /* Luft am rechten Rand der Tagesansicht. In der Wochenansicht schließen
     die Spalten bündig ab; im Tag bleibt ein schmaler Streifen, damit die
     Karte nicht an der Kante klebt. 7px waren dafür mehr als nötig. */
  const edge  = isDay ? '3px' : '0px';
  const track = `(100% - var(--gutter-w) - ${edge})`;
  /* Die Spalten liegen in einem eigenen Streifen, der bei var(--gutter-w)
     beginnt. Nur er wandert beim Wischen — die Zeitspalte bleibt stehen. */
  const colW  = `calc(100% / ${cols})`;
  const colAt = i => `calc(${i} * 100% / ${cols})`;
  const todayIdx = dates.indexOf(todayISO);

  let html = `<div class="grid" style="height:${L.total}px">`;


  /* Zeilen + Zeitspalte */
  L.rows.forEach(r => {
    html += `<div class="slotrow ${r.no ? '' : 'slotrow--pause'}" style="top:${r.y}px">
      <span class="slotrow__time">${r.label}</span>
      ${r.no ? `<span class="slotrow__no">${r.no}.</span>` : ''}
    </div>`;
  });
  html += `<div class="slotrow" style="top:${L.total}px"></div>`;
  /* Unter der letzten Stunde läuft das Raster weiter — am iPhone bis ganz
     unten hinter die Glasleiste, wie im Original. Leere Zeilen im Takt
     einer Stunde, ohne Beschriftung. */
  const takt = (() => {
    const t1 = (state.times || {})[1];
    const m = t1 ? minutes(t1.end) - minutes(t1.start) : 45;
    return Math.max(24, Math.round((m > 0 ? m : 45) * PPM));
  })();
  const sichtbar = (pane && pane.parentElement && pane.parentElement.clientHeight) || window.innerHeight;
  const rest = Math.max(0, sichtbar - L.total);
  html += `<div class="grid__rest" style="top:${L.total}px;--takt:${takt}px;--rest:${rest}px"></div>`;

  /* Der Rahmen beginnt an der Zeitspalte und schneidet ab, was darüber
     hinausragt — sonst schöbe sich die Nachbarwoche über die Uhrzeiten. */
  html += `<div class="colclip" style="right:${edge}"><div class="colstrip" id="colStrip">`;

  /* Tagesspalten */
  dates.forEach((date, ci) => {
    const source = byDate[date] || [];
    source.forEach((c, i) => { c._uid = i; });

    /* Ohne jeden anzeigbaren Wert entsteht sonst ein leerer Block. */
    const hasLabel = c => ['class','teacher','room','subject']
      .some(k => (c[k] || c[k + 'Orig'] || '').toString().trim() !== '');
    let cards = source.filter(c => {
      /* Ohne Beschriftung entstünde ein leerer Block — auch Sonderfelder
         müssen etwas anzuzeigen haben. */
      if (c.blockType === 'holiday' || c.holiday) return true;
      if (!c.lesson || !c.span) return false;
      return hasLabel(c);
    });

    /* Ferien und Feiertage überdecken den ganzen Tag — keine Stunden darunter. */
    const holiday = cards.find(c => c.blockType === 'holiday' || c.holiday);
    if (holiday) cards = [holiday];
    else {
      if (!state.opts.showCancelled) cards = cards.filter(c => c.blockType !== 'cancelled');
      cards = assignParallel(cards);

      /* In der Tagesansicht steht ein Feld links und der betroffene
         Unterricht rechts daneben. In der Wochenansicht ist dafür kein Platz;
         dort zählt allein die Rangfolge, damit der Unterricht, der wirklich
         stattfindet, die Fläche bekommt. */
      /* Diese Merkmale hängen an der Ansicht, werden aber auf den Karten
         selbst vermerkt — und die überleben den Wechsel. Ohne Zurücksetzen
         blieben Karten aus der Tagesansicht als „_side" markiert und
         verschwanden in der Wochenansicht ganz. */
      cards.forEach(c => { delete c._side; delete c._hit; delete c._unterFeld; });

      /* --- Aufsichten im Zeitraum eines Feldes (GS, AK, Veranstaltung) ----
         Entschieden wird nach Uhrzeit, nicht nach Stundennummer: Die
         Aufsicht trägt als Nummer die Stunde *vor* ihrer Pause. Nach Nummer
         zählte deshalb auch die Pause direkt nach dem Feld mit, die gar
         nicht mehr darin liegt. Aufsichten vor der ersten Stunde stehen
         oberhalb des Rasters und bleiben ohnehin außen. */
      const minuten = t => {
        const m = /^(\d{1,2}):(\d{2})/.exec(String(t || ''));
        return m ? (+m[1]) * 60 + (+m[2]) : null;
      };
      const istPause = c => !!(c._supervision || c._pause || c.blockType === 'supervision');
      const aufsichtenIm = o => {
        const vonO = minuten((state.times[o.lesson] || {}).start);
        const bisO = minuten((state.times[o.lesson + o.span - 1] || {}).end);
        if (vonO === null || bisO === null) return [];
        return cards.filter(c => {
          if (c.overlay || !istPause(c) || c._pausePre) return false;
          const a = minuten(c.from), b = minuten(c.to);
          return a !== null && b !== null && a >= vonO && b <= bisO;
        });
      };

      /* Auf breitem Bildschirm (iPad) auch in der Woche: Feld links, die
         wegen der Veranstaltung entfallenden Stunden und Aufsichten rechts daneben. */
      if (isDay || nebenFeld){
        cards.filter(c => c.overlay).forEach(o => {
          const hit = cards.filter(c => !c.overlay && !istPause(c)
            && c.lesson <= o.lesson + o.span - 1 && o.lesson <= c.lesson + c.span - 1);

          /* Liegt eine Pausenaufsicht innerhalb des Feldes — etwa die
             Hofaufsicht zwischen der 2. und 3. Stunde bei einer Freisetzung
             von der 1. bis zur 4. —, steht sie wie die Stunden als halbe
             Kachel rechts daneben.

             In die Aufteilung der Stunden (`groupOverlaps`) geht sie nicht
             ein. Nach Nummer überlappte die Aufsicht sonst mit der Stunde
             vor ihrer Pause, und beide bekämen nur ein Viertel der Breite —
             obwohl sie zeitlich gar nicht nebeneinander liegen. */
          const aufsichten = aufsichtenIm(o);

          o._i = 0; o._n = 1; o._hit = hit.length + aufsichten.length;
          hit.forEach(c => { c._side = true; });
          groupOverlaps(hit);
          aufsichten.forEach(c => { c._side = true; c._i = 0; c._n = 1; });
        });
      } else {
        /* In der Wochenansicht ist neben dem Feld kein Platz für eine halbe
           Kachel. Die Aufsicht lief dort quer über das Feld. Sie wird
           ausgeblendet — wie die Stunden, die hinter das Feld zurücktreten.
           Zu sehen ist sie in der Tagesansicht. */
        cards.filter(c => c.overlay).forEach(o =>
          aufsichtenIm(o).forEach(c => { c._unterFeld = true; }));
        cards = cards.filter(c => !c._unterFeld);
      }
    }

    const hasBand = (state.week.bands || []).some(b => b.date === date);
    const inset = hasBand ? 'var(--day-gap) + 18px' : 'var(--day-gap)';
    const dayW = `calc(${colW} - (${inset}))`;
    html += `<div class="daycol" style="left:calc(${colAt(ci)} + var(--day-gap) / 2);width:${dayW}">`;

    /* Eigene Abwesenheit („Mein Plan“): grauer Balken hinter den Stunden.
       Die Karten liegen voll lesbar darüber; „Abwesend“ steht dezent nur in
       der größten Lücke ohne Karte. Abschaltbar im ⋮-Menü. */
    if (state.entity && state.entity.type === 'mine' && state.opts.showAbsences){
      const belegt = cards.filter(c => !(c._supervision || c._pause || c._pausePre || c.blockType === 'supervision'))
        .map(c => c.from && c.to ? [atTime(c.from) + 1, atTime(c.to)]
                                 : [L.top(c.lesson) + 1, L.top(c.lesson) + L.height(c.lesson, c.span)]);
      (absences[date] || []).forEach(b => {
        const f = abwesenheitsFeld(b, belegt, atTime);
        html += `<div class="absfeld" style="top:${f.oben}px;height:${f.hoehe}px" aria-hidden="true">${f.mitText
          ? `<span class="absfeld__txt" style="top:${f.textOben}px;height:${f.textHoehe}px">${esc(b.text || 'Abwesend')}</span>` : ''}</div>`;
      });
    }

    /* Karten, die hinter die vordere Spalte zurücktreten. Sie werden erst
       gesammelt und danach zu einem Streifen je zusammenhängendem Bereich
       vereint — sonst stapeln sich mehrere Zählkarten übereinander. */
    const verdraengt = [];

    cards.forEach(card => {
      /* Wochenansicht: nur die erste Karte einer Parallelgruppe zeigen,
         daneben steht eine schmale Zählkarte mit der Anzahl der übrigen.
         Reicht der Platz nicht (Handy), tritt diese Zählkarte an die Stelle
         der verdrängten Kacheln — rot bei Entfall dahinter, grün bei einer
         Vertretung, sonst weiß. Tagesansicht: alle nebeneinander. */
      /* Verdrängte Karte: Sie tritt hinter die Karte in der ersten Spalte
         zurück. Wo diese aber gar nicht liegt — bei einem vierstündigen
         Entfall etwa, in den nur für die 3. und 4. etwas vorgezogen wurde —
         bleibt Platz. Dort wird die Zählkarte gezeichnet, statt die Stunden
         einfach leer zu lassen. */
      const inPause = !!(card._supervision || card._pause
                      || card.blockType === 'supervision');
      if (!isDay && !inPause && card._n > grenzeFuer(cards, card) && card._i > 0){
        const vorne = cards.filter(x => x._i === 0
          && x.lesson < card.lesson + card.span && card.lesson < x.lesson + x.span);
        if (vorne.length){
          /* Nicht sofort zeichnen: Mehrere verdrängte Karten hintereinander
             ergäben sonst mehrere Streifen übereinander — im Raster standen
             dort „1“, „2“, „1“ gestapelt. Gesammelt wird erst, danach wird
             pro zusammenhängendem Bereich ein einziger Streifen gesetzt. */
          /* Der Streifen gehört zu der Karte, die vorne steht — nicht zu
             den verdrängten. Nur so ist er genauso hoch wie sie. */
          verdraengt.push({ karte:card, front:vorne[0] });
          return;
        }
      }

      const isHoliday = card.blockType === 'holiday' || card.holiday;
      const isStandby = card.blockType === 'standby';
      /* Bereitschaften und Aufsichten liegen oft in einer Pause und werden
         deshalb nach Uhrzeit gesetzt, nicht nach Stundennummer. */
      const byTime = !isHoliday && card.from && card.to;

      /* Aufsichts-Pillen sitzen mittig in der Pausenlücke, mit mindestens
         1,5 px Luft nach oben und unten — nie direkt Kante an Kante.

         Eine gewöhnliche Pause dauert 20 Minuten und ist damit gut 25 px
         hoch. Von den bisher abgezogenen 6 px blieben der Pille knapp
         19 px, in denen eine 12,4-px-Zeile sitzen musste — sie wirkte
         gedrückt und schmal. Mit 3 px Abzug bleiben gut 22 px, und die
         Deckelung liegt so hoch, dass die Lücke sie bestimmt und nicht
         umgekehrt. In langen Pausen bleibt die Pille dennoch eine Pille:
         Sie soll nicht wie eine Unterrichtsstunde aussehen. */
      const SUP_H = 26;
      const top = isHoliday ? 0
                : card._pausePre ? L.pre - PRE_PILLE - PRE_LUFT
                : (byTime && card._supervision)
                    ? atTime(card.from) + Math.max(1.5, (atTime(card.to) - atTime(card.from) - SUP_H) / 2)
                : byTime    ? atTime(card.from) + 1
                :             L.top(card.lesson) + 1;
      const supGap = byTime ? (atTime(card.to) - atTime(card.from)) : 0;
      const h   = isHoliday ? L.total
                : card._pausePre ? PRE_PILLE
                : (byTime && card._supervision)
                    /* Dicht an den Stunden darüber und darunter: je ein
                       Punkt Luft – die Pille gehört sichtbar zum Plan. */
                    ? Math.max(18, Math.min(SUP_H, supGap - 1))
                : card._supervision ? SUP_H            /* Aufsicht: immer schmale Pille */
                : byTime    ? Math.max(16, atTime(card.to) - atTime(card.from) - 1)
                :             Math.max(0, L.height(card.lesson, card.span) - 1);
      /* Aufsichts-Pille exakt mittig zwischen den beiden Blöcken.

         Gemessen wird nicht die Pause nach der Uhr, sondern der Abstand,
         den man sieht: Der Block darüber endet genau an der Zeitgrenze,
         der darunter beginnt einen Punkt später — seine Oberkante ist um
         eins nach unten gesetzt. Die Lücke ist also einen Punkt höher als
         die Pausenzeit, und wer die Pille in der Pausenzeit zentriert,
         setzt sie einen halben Punkt zu hoch. Genau das war zu sehen:
         oben anderthalb Punkte Luft, unten zweieinhalb. */
      const luecke = supGap + 1;
      const topFinal = (byTime && card._supervision && !card._pausePre)
                ? atTime(card.from) + Math.max(1, (luecke - h) / 2)
                : top;
      const kind = viewKind(state.entity);

      /* Im eigenen Plan gilt: sobald die Stunde von jemand anderem übernommen
         wird — ob schon eingeteilt oder noch offen — läuft sie ohne mich.
         Dargestellt mit Eckklammern und dem eigenen Kürzel durchgestrichen.
         Das muss feststehen, bevor die Zeilen gebaut werden. */
      /* Im eigenen Plan steht als Beschriftung „Mein Stundenplan" — zum
         Vergleichen taugt nur das Kürzel dahinter. Genau hier fehlte es,
         weshalb die durchscheinende Darstellung dort ausblieb. */
      const myCode = (state.entity.code || state.entity.label || '').toUpperCase();
      const isSup = card.blockType === 'supervision' || !!card._supervision;
      /* Läuft die Stunde ohne mich (bzw. ohne die eigene Klasse)? Dieselbe
         Regel entscheidet in `assignParallel`, ob die Karte grün ist. */
      card._released = freigegeben(card, kind);

      let lines = cardLines(card, kind);
      /* Online-Unterricht: keine Raumzeile und kein Raumlabel — nur das
         Zeichen unten auf der Kachel sagt „findet online statt“. */
      const online = !!card.online && !isHoliday && !isSup && card.blockType !== 'cancelled';
      if (online) lines = lines.filter(l => l.key !== 'room');
      /* Tagesansicht wie im Original: dieselbe Kachel wie in der Woche —
         Balken, Zeilen, Plaketten —, nur breiter. Keine Zusatzdetails. */

      /* Passen nicht alle Zeilen in die Karte, wird gekürzt und unten rechts
         ein Zeichen gesetzt — dann ohne Info-Punkt. */
      const maxLines = Math.max(1, Math.floor((h - 9) / karteZeile()));
      const cut = !isHoliday && !isStandby && !card.overlay && lines.length > maxLines;
      if (cut) lines = lines.slice(0, maxLines);
      const past = (() => { const e = cardEndDate(card); return e && e < now; })();

      const cls = ['card'];
      if (card.blockType === 'change')       cls.push('card--change');
      if (card.blockType === 'cancelled')    cls.push('card--cancelled');
      if (card.blockType === 'klausur')      cls.push('card--exam');
      if (card.blockType === 'event')        cls.push('card--event');
      if (isHoliday)                         cls.push('card--holiday');
      /* Auf `_supervision` prüfen, nicht allein auf den Blocktyp: Der kann
         unterwegs überschrieben worden sein, die Kachel bleibt trotzdem eine
         Aufsicht. */
      if (isSup)                             cls.push('card--supervision');
      if (isSup)                             cls.push(...aufsichtKlassen(card));
      if (isStandby)                         cls.push('card--standby');
      if (isStandby && card.standbyAbsent)   cls.push('is-absent');
      if (card.blockType === 'absence')      cls.push('card--gs');
      if (card.blockType === 'gs')           cls.push('card--gs');
      if (card.blockType === 'class-absent') cls.push('card--class-absent');
      /* Offene Vertretung im Plan der ausgefallenen Lehrkraft: die Stunde
         läuft ohne sie — dargestellt mit Eckklammern statt grüner Fläche. */
      if (card.highlight)                    cls.push('is-highlight');

      const released = card._released;
      if (released){
        cls.push('card--released');
        /* Eine freigegebene Stunde ist kein Entfall — der durchgestrichene
           Text und der rote Rahmen dürfen nicht mitkommen. */
        ['card--change','card--cancelled','card--class-absent','is-highlight'].forEach(k => {
          const i2 = cls.indexOf(k); if (i2 > -1) cls.splice(i2, 1);
        });

      }
      /* Zurücktreten gilt für Unterricht, Vertretung, Entfall und
         Verfügungsstunden — nicht für Ferien, Termine und dergleichen,
         die sich über den ganzen Tag ziehen. */
      if (past && ['normal', 'change', 'cancelled', 'klausur', 'standby', 'supervision', 'gs', 'absence', 'event'].includes(card.blockType))
        cls.push('is-past');

      let style = `top:${topFinal}px;height:${h}px;`;
      let more = 0;
      if (card.overlay && card._hit){
        /* Genau dieselbe Aufteilung wie bei jeder anderen parallelen
           Kachel — gleiche Spaltenbreite, gleicher Zwischenraum. Vorher
           bekamen GS und AK eine feste halbe Breite ohne Abstand. */
        style += lane(Math.max(2, card._n), card._i, 0, 100, isDay ? 4 : 2);
        cls.push('is-priority');
      } else if (card._side && !isDay && !nebenFeld){
        /* Entfallene Originalstunden treten hinter das Feld zurück. */
        return;
      } else if (card._side){
        style += lane(card._n, card._i, 50, 100, isDay ? 4 : 2);
      } else if (isDay && card._n > 1 && !card._supervision){
        style += lane(card._n, card._i, 0, 100, 4);
      } else if (isDay && card._supervision){
        /* Aufsichten stehen über die volle Breite und beeinflussen die
           Aufteilung der Stunden nicht. */
      } else if (card._n > 1 && card._n <= grenzeFuer(cards, card)){
        style += lane(card._n, card._i, 0, 100, 2);
      } else if (card._n > 1){
        /* Gezählt wird, was diese Kachel wirklich verdeckt — nicht die
           gesamte Kette. Sonst steht dort eine zu hohe Zahl. */
        more = cards.filter(x => x !== card && x._i > 0
          && x.lesson < card.lesson + card.span && card.lesson < x.lesson + x.span).length;
        if (!more) { /* nichts verdeckt — volle Breite */ }
        else { style += 'width:calc(100% - 14px);'; cls.push('has-count'); }
      }

      /* wie in timetable.php: bei reinem Entfall und bei Terminen kein Info-Icon
         auf der Kachel — der Text steht in der Detailansicht. Ferien tragen den
         Punkt nie, also auch nicht die Klasse (sonst schiebt der rechte Rand
         der letzten Zeile das zentrierte Datum nach links). */
      /* Der Punkt steht auch dann, wenn zu der Stunde eine Notiz im
         Klassenbuch hinterlegt ist. */
      const hatText = (!!card.info && !/^\s*entf(ä|ae)llt\.?\s*$/i.test(card.info)) || !!card.hasNote;
      /* Beide Zeichen sitzen in derselben Ecke, es kann nur eines stehen.
         Den Vorrang hat das Kürzungszeichen — aber nur, wenn wirklich
         gekürzt wurde, also Zeilen weggefallen sind. Dann fehlt auf der
         Kachel etwas, und das wiegt schwerer als der Hinweis: Der Hinweis
         sagt „hier steht mehr“, das Kürzungszeichen sagt „hier fehlt
         etwas“. Wo nichts wegfällt, bleibt es beim Punkt. */
      /* Die Aufsichtspille trägt kein Info-Zeichen: Was es zu sagen gäbe
         („Vertretung für …“), steht dort schon in den Labels. */
      const showInfo = hatText && card.blockType !== 'event' && !isHoliday && !cut && !isSup;
      if (showInfo) cls.push('has-info');
      /* Nur kürzen, wenn der Info-Punkt die letzte Zeile wirklich erreicht.
         Auf hohen Karten steht er weit unter dem Text — dann volle Breite,
         kein „…". Erreicht der Text den Punkt, wird rechts Platz gemacht und
         der Raum (oder das Vertretungslabel) mit „…" gekürzt. */
      /* Sowohl beim Info-Punkt als auch beim „…"-Zeichen (Kürzung) Platz
         rechts lassen, damit der Raum/Text mit … abgekürzt statt verdeckt
         wird. */
      const textBottom = 4.5 + lines.length * karteZeile();
      /* Großzügig vorauswählen: Das Zeichen sitzt 2 px über dem inneren
         Rand, bei einer gerahmten Kachel kommen dessen 2 px hinzu — mit
         einer knappen Schwelle rutschten genau diese Fälle durch und das
         Zeichen legte sich über den Raum. Wer hier landet, wird nach dem
         Zeichnen genau vermessen; wo nichts im Weg steht, fällt das
         Polster dort wieder weg. */
      if ((showInfo || cut || card.hasHw || online) && textBottom > h - 21) cls.push('pad-last', 'pad-check');

      const timeA = (state.times[card.lesson] || {}).start || '';
      const timeB = (state.times[Math.min(card.lesson + card.span - 1, 12)] || {}).end || '';

      const DOTS = '<svg viewBox="0 0 12 12"><circle cx="6" cy="2.4" r="1.05"/><circle cx="6" cy="6" r="1.05"/><circle cx="6" cy="9.6" r="1.05"/></svg>';

      /* Inhalt einer Kartenzeile: Plaketten, Kopplungs-Kürzel oder Text. */
      /* Offene Vertretung (noch niemand eingeteilt): Die bisherige Lehrkraft
         steht immer durchgestrichen IN der grünen Plakette — ebenso ein noch
         offener Raum. Nur wo eine Vertretung eingeteilt ist (Kopplung), steht
         das alte Kürzel ohne Fläche und das neue grün. */
      const zeileInnen = l => {
        const offenGruen = l.removed && (l.key === 'teacher' || l.key === 'room')
          && card.blockType !== 'cancelled';
        const cls2 = 'badge' + (l.removed ? ' badge--removed' : '') + (offenGruen ? ' badge--open' : '') + (l.key === 'room' ? ' badge--raum' : '');
        return l.tokens
          ? l.tokens.map((t, ti) => {
              /* Auf der Kachel trennt immer ein Komma — auch mehrere Lehrkräfte. */
              const komma = ti < l.tokens.length - 1 ? (l.key === 'room' ? '' : l.key === 'teacher' ? '<i class="tok__trenn">,</i> ' : '<i>,</i>') : '';
              /* Hinzugekommener Wert: grünes Label mit „+“ davor. */
              const plusT = plusWert(card, l.key, t.text);
              const kern  = t.state === 'absent'
                ? `<span class="badge badge--removed${koppOffen(t, l.tokens) ? ' badge--open' : ''}">${esc(t.text)}</span>`
                : t.state === 'sub'
                /* Alle Lehrkräfte stehen da: die vertretene durchgestrichen,
                   daneben die Vertretung als grüne Plakette — auch im Plan
                   der abwesenden Lehrkraft selbst. */
                ? (t.for && l.key === 'teacher'
                    ? `<span class="badge badge--removed">${esc(t.for)}</span><i class="tok__trenn">,</i> <span class="badge">${esc(t.text)}</span>`
                    : `<span class="badge${l.key === 'room' ? ' badge--raum' : ''}">${esc(plusT)}</span>`)
                : (t.plus || plusT !== t.text)
                ? `<span class="badge${l.key === 'room' ? ' badge--raum' : ''}">${esc(plusT)}</span>`
                : esc(t.text);
              return `<span class="tok tok--${t.state}">${kern}${komma}</span>`;
            }).join('')
          : l.changed ? `<span class="${cls2}">${esc(l.removed ? l.text : werteListe(l.text).map(x => plusWert(card, l.key, x)).join(KACHEL_TRENNER))}</span>` : esc(l.text);
      };

      const body = isHoliday
        ? `<span class="holiday-inner">
             <span class="holiday-mark" aria-hidden="true">
               <svg viewBox="0 0 24 24"><rect x="5" y="10.5" width="14" height="9.5" rx="2.2"/><path d="M8.4 10.5V7.8a3.6 3.6 0 0 1 7.2 0v2.7"/></svg>
             </span>
             <span class="card__line">${esc(card.subject || 'Ferien')}</span>
             ${card.holidayName ? `<span class="card__line holiday-name">${esc(card.holidayName)}</span>` : ''}
             ${card.holidayRange ? `<span class="card__line holiday-range">${esc(card.holidayRange)}</span>` : ''}
           </span>`
        /* Bereitschaft der abwesenden Lehrkraft: Das eigene Kürzel steht
           durchgestrichen im grünen Label — dieselbe Sprache wie bei einer
           offenen Vertretung. */
        : (isStandby && card.standbyAbsent)
        ? `<span class="card__line card__line--primary">${esc(card.subject || 'Bereitschaft')}</span>
           ${card.teacherOrig ? `<span class="card__line card__line--badge"><span class="badge badge--removed badge--open">${esc(card.teacherOrig)}</span></span>` : ''}`
        /* Pausenaufsicht: immer nur der Ort; bei einer Vertretung
           (eingesprungen oder abgegeben) ganz rechts das grüne „…“ – wer
           vertritt, steht im Detail. Entfallen: Entfall-Stil, kein Zeichen. */
        : (card.blockType === 'supervision' || card._supervision)
        ? aufsichtInhalt(card)
        : isStandby
        ? `<span class="card__line">${esc(card.subject || 'Bereitschaft')}</span>`
        : (card.blockType === 'absence' || card.blockType === 'gs')
        /* Veranstaltung, Klassenabwesenheit, Freisetzung: dieselbe Regel
           wie beim Unterricht. Oben steht, was man im eigenen Plan nicht
           ohnehin weiß — im Klassenplan die Lehrkraft, sonst die Klasse —,
           darunter der Anlass und im Lehrerplan zuletzt die begleitenden
           Kolleginnen und Kollegen. Vorher stand überall die Klasse oben,
           und der Anlass fehlte auf der Kachel ganz, wenn er nur im
           Zusatztext stand. */
        ? (() => {
            const anlass = (card.subject || card.info || '').trim();
            const eigen  = (state.entity.code || state.entity.label || '').toUpperCase();
            const alle   = String(card.teacher || '').split(/\s*,\s*/).filter(Boolean);
            const klassen= String(card.class || '').split(/\s*,\s*/).filter(Boolean);
            const oben   = kind === 'class' ? alle.join(KACHEL_TRENNER) : klassen.join(KACHEL_TRENNER);
            /* Unten steht, wer sonst noch dabei ist: im Klassenplan die
               übrigen Klassen, im Lehrerplan die übrigen Lehrkräfte — die
               eigene Klasse beziehungsweise das eigene Kürzel jeweils ohne.
               Fahren drei Klassen zusammen zu einem Vortrag, soll jede
               sehen, mit wem. */
            const unten  = kind === 'class'
                         ? klassen.filter(k => k.toUpperCase() !== eigen).join(', ')
                         : alle.filter(t => kind !== 'teacher' || t.toUpperCase() !== eigen).join(KACHEL_TRENNER);
            /* Mehrere Lehrkräfte: auf der Kachel durch Komma getrennt (in den
               Details „ | “); nur diese Leiste rollt waagerecht. */
            const rollen = t => t.includes(KACHEL_TRENNER) ? ' card__line--scroll' : '';
            return [
              oben   ? `<span class="card__line card__line--primary${kind === 'class' ? rollen(oben) : ''}">${esc(oben)}</span>` : '',
              anlass ? `<span class="card__line ${oben ? '' : 'card__line--primary'}">${esc(anlass)}</span>` : '',
              unten  ? `<span class="card__line ${card.room ? '' : 'card__line--last'}${kind !== 'class' ? rollen(unten) : ''}">${esc(unten)}</span>` : '',
              /* Raum der Veranstaltung */
              card.room ? `<span class="card__line card__line--last">${esc(card.room)}</span>` : ''
            ].filter(Boolean).join('');
          })()
        : zeilenHtml(lines);
      function zeilenHtml(lines){ return lines.map((l, li) => {
            const inner = zeileInnen(l);
            return `<span class="card__line ${l.primary ? 'card__line--primary' : ''}`
                 /* Beginnt die Zeile mit einer Plakette, bringt die ihr
                    eigenes Polster schon mit. Die Zeile darf dann keines
                    mehr haben — sonst steht der erste Wert 1,5 px weiter
                    rechts als jede andere Plakette auf der Kachel. */
                 + `${(l.changed || (l.tokens && (l.tokens[0].state || 'ok') !== 'ok'))
                      ? ' card__line--badge' : ''}`
                 + `${l.tokens ? ' card__line--kopp' : ''}${l.tokens && l.key === 'room' ? ' card__line--rooms' : ''}${l.tokens && l.key === 'teacher' ? ' card__line--scroll' : ''}`
                 + `${li === lines.length - 1 ? ' card__line--last' : ''}">${inner}</span>`;
          }).join(''); }
      /* Die Fachfarbe steht als Stilangabe unmittelbar an der Kachel.
         Sie gilt auch beim Entfall: Ist für das Fach eine Farbe
         hinterlegt, bleibt der Balken farbig — erkennbar ist der Entfall
         an Rahmen, Fläche und Durchstreichung. Welche Farbe es ist,
         entscheidet der Server: beim Entfall die des ausfallenden Fachs,
         nicht die der Stunde, die stattdessen läuft. */
      if (card.color && /^#?[0-9a-f]{3,8}$/i.test(card.color))
        style += `--bar:${card.color.startsWith('#') ? card.color : '#' + card.color};`;

      /* --- Was in der unteren rechten Ecke steht -------------------------
         Drei Dinge bewerben sich um dieselbe Ecke: der Info-Punkt, das
         Hausaufgabenzeichen und das Kürzungszeichen. Die Regel:

           · Wurde wirklich gekürzt, gewinnt das Kürzungszeichen allein —
             dann fehlt auf der Kachel etwas, und das wiegt am schwersten.
           · Sonst stehen Info und Hausaufgaben nebeneinander, solange die
             Kachel breit genug ist. Zwei Zeichen brauchen gut vierzig
             Pixel; darunter stünden sie im Text.
           · Passen beide nicht nebeneinander, steht das Kürzungszeichen
             dort — es sagt „hier ist mehr“, und ein Tippen führt zum
             Detail, wo beides ausgeschrieben steht.

         `colPx` ist die Breite einer Tagesspalte, `_n` die Anzahl der
         Kacheln, die sich diese Spalte teilen — zwei nebeneinander haben
         also je die halbe Breite. */
      const zeigHw = !!card.hasHw && !isHoliday && !isStandby && !isSup;
      const zeigInfo = showInfo && !isHoliday;
      const kachelBreit = colPx / Math.max(1, card._n || 1);

      let eckzeichen = '';
      const dots = `<span class="card__cut" aria-hidden="true">${DOTS}</span>`;
      /* Zeichen unten rechts: Online, Hausaufgabe, Info — in dieser
         Reihenfolge von links nach rechts. Reicht die Breite nicht für alle
         (je 14 px und 3 px Abstand, links bleibt der Balken frei), fällt
         zuerst die Hausaufgabe weg, dann das Onlinezeichen; Info bleibt am
         längsten stehen. Wurde wirklich gekürzt, steht allein das
         Kürzungszeichen dort. Ein Verlegungszeichen gibt es auf der Kachel
         nicht (wie iOS) – die Verlegung steht in den Details. */
      const platz = Math.max(1, Math.floor((kachelBreit - 18 + 3) / 17));
      const vorrang = [zeigInfo && 'info', online && 'online', zeigHw && 'hw'].filter(Boolean).slice(0, platz);
      const ikon = n => n === 'online' ? `<span class="card__ic card__ic--online">${KACHEL_ONLINE}</span>`
                      : n === 'hw' ? `<span class="card__ic card__ic--exakt">${KACHEL_HA}</span>`
                      : `<span class="card__ic card__ic--info">${KACHEL_INFO}</span>`;

      if (cut && !card._supervision && card.blockType !== 'supervision'){
        eckzeichen = dots;
      } else if (vorrang.length){
        eckzeichen = `<span class="card__ecke">${['online', 'hw', 'info'].filter(n => vorrang.includes(n)).map(ikon).join('')}</span>`;
      }
      if (online) cls.push('has-online');

      const tag = isHoliday ? 'div' : 'button';
      html += `<${tag} class="${cls.join(' ')}" style="${style}"
                 data-date="${date}" data-lesson="${card.lesson}" data-idx="${card._uid}"
                 ${isHoliday ? '' : 'type="button"'}>
        ${body}
        ${eckzeichen}
        ${(released || (isStandby && card.standbyAbsent) || (isSup && card._supAbsent)) ? '<span class="corners" aria-hidden="true"><i></i><i></i><i></i><i></i></span>' : ''}
        ${(isHoliday || isSup) ? '' : `<span class="card__times"><span>${timeA}</span><span>${timeB}</span></span>`}
      </${tag}>`;

      /* Gezeichnet wird die Zählkarte **nicht** hier, sondern weiter unten
         beim Zusammenfassen. Beide Wege nebeneinander waren die Ursache
         der doppelten Streifen: Diese Stelle setzte einen je vorderer
         Kachel, das Zusammenfassen einen je zusammenhängendem Bereich —
         und beide beschrieben dieselben verdeckten Stunden. Hier bleibt
         allein das Schmalermachen der vorderen Kachel, damit der Streifen
         daneben Platz hat. */
    });

    /* --- Zählkarten zusammenfassen -------------------------------------
       Alles, was sich berührt oder überlappt, wird zu einem Streifen. Die
       Zahl sagt, wie viele Karten dahinter liegen. */
    if (verdraengt.length){
      /* Je vorderer Karte ein Streifen, so hoch wie sie. Ein durchgehender
         Vierstundenblock bekommt damit einen durchgehenden Streifen; stehen
         dort zwei getrennte Stunden, sind es zwei — auch wenn sie
         aneinandergrenzen. Vorher entschied der Abstand der verdrängten
         Karten, und der passte mal zum einen, mal zum anderen Fall. */
      const proFront = new Map();
      verdraengt.forEach(({ karte, front }) => {
        if (!proFront.has(front)) proFront.set(front, []);
        proFront.get(front).push(karte);
      });
      /* Die Höhe richtet sich nach dem, was tatsächlich dahinterliegt:
         Betrifft die Vertretung nur zwei Stunden eines Vierstundenblocks,
         reicht der Streifen auch nur über diese zwei. Zusammengefasst wird
         dabei über eine Pause hinweg — die Zuordnung zur vorderen Karte hat
         schon entschieden, was zusammengehört. */
      const roh = [...proFront.entries()].map(([front, karten]) => ({
        von: Math.min(...karten.map(k => k.lesson)),
        bis: Math.max(...karten.map(k => k.lesson + k.span - 1)),
        karten
      })).sort((a, b) => a.von - b.von);

      /* Nur **überlappende** Streifen zu einem verschmelzen.
         Die Zuordnung geschieht je vorderer Kachel, und zwei vordere
         Kacheln können sich in denselben Stunden überschneiden — etwa
         wenn ein Feld über den ganzen Tag läuft und daneben eine
         Vertretung. Dann stünden zwei Zahlen übereinander.

         Aneinandergrenzende Streifen bleiben getrennt. Zwei Stunden, die
         sich lediglich berühren, sind zwei Stunden: Die 2. und die 3.
         gehören nicht zusammen, nur weil zwischen ihnen keine Lücke
         steht. Genau das hatte ich mit `bis + 1` versehentlich
         zusammengezogen. */
      const streifen = [];
      roh.forEach(st => {
        const letzter = streifen[streifen.length - 1];
        if (letzter && st.von <= letzter.bis){
          letzter.bis = Math.max(letzter.bis, st.bis);
          st.karten.forEach(k => { if (!letzter.karten.includes(k)) letzter.karten.push(k); });
        } else {
          streifen.push({ von: st.von, bis: st.bis, karten: st.karten.slice() });
        }
      });

      streifen.forEach(st => {
        const t  = L.top(st.von) + 1;
        const hh = Math.max(0, L.height(st.von, st.bis - st.von + 1) - 1);
        const ton = st.karten.some(x => x.blockType === 'cancelled') ? ' parcount--cancel'
                  : st.karten.some(x => x.blockType === 'change')    ? ' parcount--change' : '';
        const vorbei = st.karten.every(x => {
          const e = cardEndDate(x); return e && e < now;
        });
        html += `<button class="parcount${ton}${vorbei ? ' is-past' : ''}"
                   style="top:${t}px;height:${hh}px"
                   data-more="${date}" type="button">${st.karten.length}</button>`;
      });
    }

    html += '</div>';
  });

  /* Abwesenheitsbänder */
  (state.week.bands || []).forEach(b => {
    const ci = dates.indexOf(b.date);
    if (ci < 0) return;
    const top = L.top(b.from);
    const h   = L.height(b.from, b.to - b.from + 1);
    html += `<div class="absband" data-band="${b.date}"
               style="top:${top}px;height:${h}px;left:calc(${colAt(ci)} + ${colW} - 18px - var(--day-gap) / 2)">${b.count || ''}</div>`;
  });

  /* Jetzt-Linie: dünn über die ganze (aktuelle) Woche, am heutigen Tag
     etwas kräftiger.

     Der Streifen trägt drei Wochen (bzw. drei Tage) nebeneinander — sichtbar
     ist immer nur die mittlere. Steht „heute" in einer der Nachbarspalten,
     gehört gar keine Linie ins Bild: weder die kräftige noch die dünne.
     Sonst läge der dünne Balken über einer Woche, in der es „jetzt" gar
     nicht gibt. */
  const ti     = dates.indexOf(todayISO);
  const midAt  = cols;                     /* erste Spalte des mittleren Abschnitts */
  const midLen = cols;
  if (ti >= midAt && ti < midAt + midLen){
    const mins = now.getHours()*60 + now.getMinutes();
    const y = (mins - L.t0) * PPM + L.pre;
    if (y >= L.pre && y <= L.total){
      const wkStart = midAt;
      const wkCols  = midLen;
      html += `<div class="nowline nowline--week" style="top:${y}px;left:${colAt(wkStart)};width:calc(${colW} * ${wkCols})"></div>`;
      html += `<div class="nowline nowline--today" style="top:${y}px;left:${colAt(ti)};width:${colW}"></div>`;
    }
  }

  /* Gesperrte Wochen (Wochenbegrenzung der Rolle): Über den Tagen der
     Woche steht mittig ein weißer Kreis mit Schloss, darunter der Text und
     kleiner der Hinweis des Servers. Das Raster bleibt, die Wochen-
     navigation auch — man kann zurück in eine freie Woche wischen. */
  const wocheZu = d => {
    const mo = iso(mondayOf(parseISO(d)));
    if (mo === state.weekStart) return state.week;
    const off = Math.round((parseISO(mo) - parseISO(state.weekStart)) / 864e5 / 7);
    return off === -1 ? around.prev : off === 1 ? around.next : null;
  };
  const sperrY = Math.max(16, Math.round(Math.min(L.total, sichtbar) / 2 - 90));
  for (let i = 0; i < dates.length;){
    const w = wocheZu(dates[i]);
    const mo = iso(mondayOf(parseISO(dates[i])));
    let j = i + 1;
    while (j < dates.length && iso(mondayOf(parseISO(dates[j]))) === mo) j++;
    if (w && w.gesperrt){
      html += `<div class="sperre" style="left:${colAt(i)};width:calc(${colW} * ${j - i})">
        <div class="sperre__inhalt" style="top:${sperrY}px">
          <div class="sperre__karte">
            <span class="sperre__kreis" aria-hidden="true">${SPERRE_SCHLOSS}</span>
            <p class="sperre__text">${SPERRE_TITEL}</p>
            <p class="sperre__hinweis">${esc(sperrErklaerung(w.sperrhinweis))}</p>
          </div>
        </div>
      </div>`;
    }
    i = j;
  }

  html += '</div></div>';  /* colstrip, colclip */
  html += '</div>';        /* grid */
  /* Nur austauschen, was sich wirklich geändert hat (siehe rasterEinsetzen):
     Beim Minutentakt, nach stillem Nachladen oder beim Zurückkehren in die
     App bleiben fast alle Kacheln stehen. Vermessen werden dann nur die
     neuen Kacheln. */
  const neu = rasterEinsetzen(pane, html);
  if (neu) feinKuerzung(neu === true ? null : neu);

  /* Der Streifen steht auf der Mitte — bei der Woche auf der aktuellen
     Woche, in der Tagesansicht auf dem gewählten Tag. Vorher stand die
     Tagesansicht auf Spalte 0 (dem Vortag): richtiges Datum, falsche Karten. */
  const strip = $('#colStrip');
  if (strip){
    /* Beim ersten Setzen KEIN Übergang — sonst gleitet der frische Streifen
       von 0 nach -100% und es sieht aus, als ginge die Woche wieder ein
       Stück zurück. */
    stripRuhe(strip);
    /* Die Datumsleiste wird vor dem Raster gebaut und kennt dessen Breite
       dann noch nicht — beim Wechsel zwischen Wochen- und Tagesansicht
       stand sie dadurch kurz drei Punkte daneben. Jetzt wird sie am Ende
       noch einmal auf dieselbe Ruhelage gesetzt. */
    stripRuhe($('#headStrip'));
  }

}

/* --- Kürzen nur dort, wo das Zeichen wirklich im Weg steht -------------
   In der unteren rechten Ecke der Kachel sitzt entweder der Info-Punkt
   oder das Kürzungszeichen. Ob eine Zeile dorthin reicht, weiß beim
   Zusammenbauen niemand: Die Spaltenbreite steht erst nach dem Einfügen
   fest, und wie breit ein Wort in der geladenen Schrift wird, erst recht.
   Deshalb merkt sich die Kachel mit `pad-check` nur, dass sie in Frage
   kommt, und wird hier vermessen.

   Gemessen werden die Kästen selbst, statt mit angenommenen Abständen zu
   rechnen: die tatsächliche Lage des Zeichens und, Kind für Kind, die
   tatsächliche rechte Kante des Zeileninhalts. Der Umweg über die Kinder
   ist nötig, weil ein Bereich über den ganzen Inhalt in WebKit kein
   brauchbares Rechteck liefert, sobald die Zeile allein aus einem Element
   besteht — und das ist bei jeder Plakette der Fall.

   Wichtig ist der Fall, in dem der Plan gar nicht im Bild ist: Ein
   verborgener Bildschirm hat keine Ausmaße, der Browser liefert lauter
   Nullen, und jede Überschneidung sähe aus wie keine. Solange nichts zu
   messen ist, gilt darum die Vorauswahl; nachgemessen wird, sobald der
   Plan sichtbar wird. */
/**
 * Den Text eines Elements so weit kürzen, dass es vor `grenze` endet —
 * mit Auslassungspunkten am Ende.
 *
 * Gesucht wird die längste Fassung, die noch passt. Ein Zeichen nach dem
 * anderen wegzunehmen wäre bei langen Werten unnötig teuer; halbiert man
 * stattdessen den Suchbereich, sind es eine Handvoll Schritte. Gemessen
 * wird dabei jedes Mal wirklich — Schriftbreiten lassen sich nicht
 * ausrechnen.
 */
function kuerzeText(el, grenze){
  const voll = el.dataset.voll != null ? el.dataset.voll : (el.dataset.voll = el.textContent);
  el.textContent = voll;
  if (el.getBoundingClientRect().right <= grenze) return;

  let lo = 0, hi = voll.length;
  while (lo < hi){
    const m = Math.ceil((lo + hi) / 2);
    el.textContent = voll.slice(0, m) + '…';
    if (el.getBoundingClientRect().right <= grenze) lo = m; else hi = m - 1;
  }
  el.textContent = lo > 0 ? voll.slice(0, lo) + '…' : '…';
}

function feinKuerzung(nur){
  const pane = $('#gridPane');
  if (!pane) return;
  /* Nur die neu eingesetzten Knoten vermessen, wenn eine Liste kommt —
     sonst (erster Aufbau, Sichtbarwerden, Aufruf aus requestAnimationFrame
     mit Zeitstempel) das ganze Raster. */
  const teil = Array.isArray(nur);
  const unter = sel => teil
    ? nur.flatMap(n => (n.matches && n.matches(sel)) ? [n] : [...n.querySelectorAll(sel)])
    : $$(sel, pane);

  /* Drei Durchgänge, damit Lesen und Schreiben nicht abwechseln (sonst
     erzwingt jede Kachel eine eigene Layoutberechnung):
       1. alles zurücksetzen (nur schreiben),
       2. alles vermessen (nur lesen),
       3. Breiten setzen (nur schreiben); Plaketten-Text kürzen braucht
          Messen und kommt nur bei wirklich zu langen Werten vor. */
  /* Welches Zeichen in der Ecke steht: Info und Hausaufgaben stehen in
     `.card__ecke`, das Kürzungszeichen in `.card__cut`; maßgeblich ist die
     linke Kante, bei zwei Zeichen nebeneinander also die des ersten. */
  const pruef = unter('.card.pad-check').map(k => ({
    k, zeichen: k.querySelector('.card__ecke, .card__cut, .card__info'), zeilen: $$('.card__line', k)
  })).filter(x => x.zeichen && x.zeilen.length);

  /* 1. Zurücksetzen — sonst misst man das Ergebnis der letzten Messung mit. */
  pruef.forEach(({ k, zeilen }) => {
    zeilen.forEach(z => {
      z.style.width = '';
      z.querySelectorAll('.badge[data-voll]').forEach(el => { el.textContent = el.dataset.voll; });
    });
    k.classList.remove('pad-last');
  });

  /* 2. Vermessen. Nicht nur die letzte Zeile: Auf einer Vertretung liegt der
     lange Raumname oft in der vorletzten, und auch die reicht auf einer
     flachen Kachel unter das Zeichen. Gemessen wird Kind für Kind, weil ein
     Bereich über den ganzen Inhalt in WebKit kein brauchbares Rechteck
     liefert, sobald die Zeile allein aus einer Plakette besteht. */
  const arbeit = [];
  pruef.forEach(({ k, zeichen, zeilen }) => {
    const ico = zeichen.getBoundingClientRect();
    /* Nichts zu messen (Plan verborgen): Vorauswahl, später nachmessen. */
    if (!ico.width || !ico.height){ arbeit.push({ k, padLast:true }); return; }
    zeilen.forEach(z => {
      const zeile = z.getBoundingClientRect();
      if (zeile.bottom <= ico.top + 1) return;          /* liegt über dem Zeichen */
      let rechts = 0;
      z.childNodes.forEach(kind => {
        if (kind.nodeType === 1){
          rechts = Math.max(rechts, kind.getBoundingClientRect().right);
        } else if (kind.nodeType === 3 && kind.nodeValue.trim()){
          try {
            const r = document.createRange();
            r.selectNode(kind);
            rechts = Math.max(rechts, r.getBoundingClientRect().right);
          } catch (e){}
        }
      });
      /* Nichts messbar: lieber polstern als das Zeichen auf den Wert legen. */
      if (!rechts) rechts = zeile.right;
      if (rechts <= ico.left + 2) return;
      const grenze = ico.left - 2;
      arbeit.push({ z, grenze, breite: Math.max(12, Math.floor(grenze - zeile.left)) });
    });
  });

  /* 3. Schreiben. Reiner Text: Die Zeile wird schmaler, `text-overflow`
     setzt die Auslassungspunkte. Plaketten würden hart abgeschnitten — ihr
     Text wird gekürzt statt ihr Kasten (kein senkrechtes Verrutschen). */
  arbeit.forEach(w => { if (w.padLast) w.k.classList.add('pad-last'); else w.z.style.width = w.breite + 'px'; });
  arbeit.forEach(w => { if (w.z) w.z.querySelectorAll('.badge').forEach(el => kuerzeText(el, w.grenze)); });

  /* Allgemein: Ein Label, das über den Rand der Kachel hinausreicht, endet
     mit „…“ statt hart abgeschnitten zu werden. Die rollbare Lehrerleiste
     bleibt ungekürzt. Erst alles lesen, dann nur die Überläufer kürzen. */
  const ueber = [];
  unter('.card').forEach(k => {
    const badges = k.querySelectorAll('.card__line:not(.card__line--scroll) .badge');
    if (!badges.length) return;
    const rand = k.getBoundingClientRect().right - 4;
    badges.forEach(el => {
      const r = el.getBoundingClientRect().right;
      if (el.dataset.voll != null && el.textContent !== el.dataset.voll && r <= rand) return;
      if (r > rand) ueber.push([el, rand]);
    });
  });
  ueber.forEach(([el, rand]) => kuerzeText(el, rand));
}

/* Kurzer Impuls am Gerät — Bestätigung ohne Ton. Bisher stand er nur in
   der Wischgeste eingeschlossen; die Schülerliste braucht ihn auch. */
const buzz = () => { if (navigator.vibrate) try { navigator.vibrate(6); } catch (e){} };

const esc = s => String(s ?? '').replace(/[&<>"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));

/* =====================================================================
   Woche laden + zeichnen
   ===================================================================== */
let renderToken = 0;
/* Schmaler Balken unter der Kopfzeile — er erscheint erst nach einer
   kurzen Weile, damit er bei schnellen Antworten gar nicht auffällt. */
let ladeTimer = null, ladeTiefe = 0;

function ladenAn(){
  if (++ladeTiefe > 1) return;
  clearTimeout(ladeTimer);
  ladeTimer = setTimeout(() => {
    const el = $('#loadBar'); if (el) el.classList.add('is-on');
  }, 180);
}
function ladenAus(){
  ladeTiefe = Math.max(0, ladeTiefe - 1);
  if (ladeTiefe) return;
  clearTimeout(ladeTimer);
  const el = $('#loadBar'); if (el) el.classList.remove('is-on');
}

/* Alles neu zeichnen, was zur Woche gehört. */
function wocheZeichnen(keepTop){
  if (tageAnsicht() && !weekDates(state.weekStart).includes(state.day))
    state.day = pickDefaultDay();
  renderDayBar();
  renderWeekHead();
  renderGrid();
  renderStart();
  $('#gridScroll').scrollTop = keepTop;
}

async function refresh(){
  const my = ++renderToken;
  const scroller = $('#gridScroll');
  const keepTop = scroller.scrollTop;      /* Wochenwechsel scrollt nicht */

  /* Zuerst die Beschriftung — wer den Plan wechselt, soll sofort sehen,
     wessen Plan geladen wird, und nicht erst, wenn die Daten da sind.
     Vorher stand hier noch minutenlang der alte Name über neuen Daten. */
  setzeBeschriftung();

  /* Liegt die Woche schon im Speicher, steht sie sofort da — ohne Balken,
     ohne Abfrage. Vorher wurde an dieser Stelle grundsätzlich geladen,
     auch wenn dieselbe Woche eine Sekunde vorher noch zu sehen war. */
  const gemerkt = wocheGemerkt(state.entity, state.weekStart);
  if (gemerkt){
    state.week = gemerkt.daten;
    nachbarnAusSpeicher();
    wocheZeichnen(keepTop);
    preloadNeighbours(my);
    if (wocheFrisch(gemerkt)) return;

    /* Zu alt: still auffrischen und nur dann neu zeichnen, wenn sich
       tatsächlich etwas geändert hat. */
    const neuWoche = await loadWeekQuiet(state.entity, state.weekStart, my).catch(() => null);
    if (my !== renderToken || !neuWoche) return;
    if (neuWoche.key && neuWoche.key !== weekKey(state.entity, state.weekStart)) return;
    if (JSON.stringify(neuWoche.byDate) === JSON.stringify(state.week.byDate)
        && JSON.stringify(neuWoche.absences || {}) === JSON.stringify(state.week.absences || {})
        && !!neuWoche.gesperrt === !!state.week.gesperrt
        && (neuWoche.sperrhinweis || '') === (state.week.sperrhinweis || '')) return;
    state.week = neuWoche;
    nachbarnAusSpeicher();
    wocheZeichnen(keepTop);
    return;
  }

  /* Die vorgeladenen Nachbarwochen gehören zum Stand von eben. Sobald
     neu geladen wird, sind sie nicht mehr belastbar. */
  state.around = {}; state.aroundKey = null;

  ladenAn();
  let week;
  try { week = await loadWeekShared(state.entity, state.weekStart, my); }
  finally { ladenAus(); }
  if (my !== renderToken) return;
  if (week.key && week.key !== weekKey(state.entity, state.weekStart)) return;
  state.week = week;
  nachbarnAusSpeicher();
  wocheZeichnen(keepTop);
  preloadNeighbours(my);
}

/* Auf einen anderen Plan umschalten.
   ---------------------------------------------------------------------
   Die Daten des bisherigen Plans werden sofort verworfen und das Raster
   leer neu gezeichnet. Sie stehenzulassen, bis die neuen eintreffen,
   hieße: Über dem neuen Namen stehen für einen Moment die Stunden des
   alten — und wer in dem Moment eine Kachel antippt, öffnet Daten aus
   einem fremden Plan.
   Der Zeitraum bleibt dabei stehen: Wer in einer anderen Woche (bzw. an
   einem anderen Tag) den Plan wechselt, sieht den neuen Plan in genau
   dieser Woche — es wird nicht zur heutigen Woche gesprungen. */
function planWechseln(neu){
  if (!neu) return;
  const gleich = planKey(neu) === planKey(state.entity);
  state.entity = neu;                    /* state.weekStart/state.day bleiben */
  store.set(state.entityKey || 'entity', { type:neu.type, id:neu.id });
  if (!gleich){
    renderToken++;                       /* laufende Ladungen abhängen */
    state.week      = { byDate:{}, bands:[] };
    state.around    = {};
    state.aroundKey = null;
    setzeBeschriftung();
    renderDayBar(); renderWeekHead(); renderGrid();
  }
  refresh();
}

/* Beim eigenen Plan steht der Name über der Klasse — sonst weiß man
   nicht, wessen Plan man ansieht. */
function setzeBeschriftung(){
  const el = $('#entityLabel');
  if (el) el.textContent =
    (state.entity.type === 'mine' && state.isStudent && BOOT.own && BOOT.own.info)
      ? BOOT.own.info
      : state.entity.label;
  const b = $('#btnInfo');
  if (b) b.hidden = !state.entity.info;
}

/* =====================================================================
   Helles oder dunkles Aussehen
   ---------------------------------------------------------------------
   'auto' folgt dem Gerät, 'light' und 'dark' überstimmen es. Die Klasse
   .is-dark auf <html> schaltet den Farbblock im Stylesheet.
   ===================================================================== */
const THEMES = ['auto', 'light', 'dark'];
const THEME_LABEL = { auto:'Automatisch', light:'Hell', dark:'Dunkel' };

function applyTheme(mode){
  const dark = mode === 'dark' ||
    (mode !== 'light' && window.matchMedia('(prefers-color-scheme: dark)').matches);
  document.documentElement.classList.toggle('is-dark', dark);
  const meta = document.querySelector('meta[name="theme-color"]');
  if (meta) meta.content = dark ? '#000000' : '#ffffff';
}

function currentTheme(){ return store.get('theme', 'auto'); }

function setTheme(mode){
  store.set('theme', mode);
  applyTheme(mode);
  /* Die Anzeige übernimmt der Umschalter selbst. */
  const seg = $('#themeSeg');
  if (seg) $$('#themeSeg button').forEach(b =>
    b.classList.toggle('is-on', b.dataset.theme === mode));
}

applyTheme(currentTheme());
window.matchMedia('(prefers-color-scheme: dark)')
  .addEventListener('change', () => applyTheme(currentTheme()));

const sleep = ms => new Promise(r => setTimeout(r, ms));

/* Öffnet den Plan dort, wo der Tag gerade steht — nicht bei der 1. Stunde. */
function scrollToNow(){
  const sc = $('#gridScroll');
  if (!sc) return;
  const L = buildLayout(state.times);
  const now = new Date();
  const y = (now.getHours() * 60 + now.getMinutes() - L.t0) * PPM + (L.pre || 0);
  const max = Math.max(0, sc.scrollHeight - sc.clientHeight);
  const ziel = Math.min(Math.max(0, y - sc.clientHeight * 0.35), max);
  /* Nie mitten in einer Stunde anschneiden: auf den Beginn der Stunde
     einrasten, die an dieser Stelle beginnt (bzw. davor). Vor der ersten
     Stunde steht der Plan ganz oben. */
  const grid = sc.querySelector('.grid');
  const off = grid ? grid.offsetTop : 0;
  const starts = (L.rows || []).filter(r => r.no).map(r => r.y + off);
  let top = 0;
  for (const st of starts) if (st - 2 <= ziel) top = st - 2;
  if (!starts.length || ziel <= (starts[0] || 0)) top = 0;
  sc.scrollTop = Math.max(0, Math.min(top, max));
}

/* ---------------------------------------------------------------------
   Wochen- und Tagesansicht
   --------------------------------------------------------------------- */
const WD_LONG = ['Montag','Dienstag','Mittwoch','Donnerstag','Freitag','Samstag','Sonntag'];

function setView(view, day){
  state.view = view;
  store.set('view', view);
  if (tageAnsicht()){
    state.day = schultag(day || state.day || pickDefaultDay());
    /* Der Tag muss in der geladenen Woche liegen */
    if (!weekDates(state.weekStart).includes(state.day)){
      state.weekStart = iso(mondayOf(parseISO(state.day)));
      refresh();
      return;
    }
  }
  const sc = $('#gridScroll'), keep = sc.scrollTop;
  renderDayBar();
  renderWeekHead();
  renderGrid();
  sc.scrollTop = keep;
}

function pickDefaultDay(){
  const dates = weekDates(state.weekStart);
  const t = iso(new Date());
  return dates.includes(t) ? t : dates[0];
}

function renderDayBar(){
  /* In der Tagesansicht steht der Tag bereits im Kopf — die zusätzliche
     Leiste entfällt, wie in der Vorlage. Gewechselt wird per Wischen,
     zurück durch Tippen auf den Tag. */
  const bar = $('#dayswitch');
  bar.hidden = true;
  if (!tageAnsicht()) return;
  const d = parseISO(state.day);
  $('#dayswitchLabel').textContent =
    `${WD_LONG[(d.getDay() + 6) % 7]}, ${d.getDate()}. ${MONTHS_SHORT[d.getMonth()]}`;
}

/* Der nächste beziehungsweise vorige Schultag. */
function shiftDay(isoDay, delta){
  let d = addDays(parseISO(isoDay), delta);
  while (d.getDay() === 0 || d.getDay() === 6) d = addDays(d, delta > 0 ? 1 : -1);
  return iso(d);
}

/* Ein Schultag: Samstag und Sonntag werden auf den folgenden Montag gelegt. */
function schultag(isoDay){
  const d = parseISO(isoDay);
  if (d.getDay() === 6) return iso(addDays(d, 2));
  if (d.getDay() === 0) return iso(addDays(d, 1));
  return isoDay;
}

/* Mehrere Schultage weiter (delta < 0: zurück). */
function tageWeiter(isoDay, delta){
  let d = isoDay;
  for (let i = 0; i < Math.abs(delta); i++) d = shiftDay(d, Math.sign(delta));
  return d;
}

/* Streifen der Tagesansichten: n Schultage davor, die n gezeigten, n danach. */
function tagesStreifen(start, n){
  const out = [tageWeiter(start, -n)];
  while (out.length < 3 * n) out.push(shiftDay(out[out.length - 1], 1));
  return out;
}

function stepDay(delta){
  /* Je so viele Schultage, wie die Ansicht zeigt. */
  const target = tageWeiter(state.day, delta * (tageAnsicht() || 1));
  const d = parseISO(target);
  if (!weekDates(state.weekStart).includes(target)){
    state.weekStart = iso(mondayOf(d));
    state.day = target;
    refresh(delta);
    return;
  }
  setView(tageAnsicht() ? state.view : 'day', target);
}

$('#dayPrev').addEventListener('click', () => stepDay(-1));
$('#dayNext').addEventListener('click', () => stepDay(1));
$('#dayBack').addEventListener('click', () => setView('week'));

/* Die Wochen links und rechts liegen schon bereit, damit beim Wischen
   nichts nachgeladen werden muss. */
async function preloadNeighbours(token){
  const entity = state.entity;
  const basis  = state.weekStart;
  const wochen = [-7, 7].map(d => iso(addDays(parseISO(basis), d)));
  /* Beide Wochen gleichzeitig — nacheinander dauerte es doppelt so lang,
     und gewartet wird ohnehin nur im Hintergrund. Was schon im Speicher
     liegt und frisch genug ist, wird gar nicht erst angefragt: Beim Hin-
     und Herwischen war das bisher jedes Mal dieselbe Abfrage. */
  const [prev, next] = await Promise.all(wochen.map(ws => {
    const g = wocheGemerkt(entity, ws);
    if (wocheFrisch(g)) return g.daten;
    return loadWeekQuiet(entity, ws, token).catch(() => (g ? g.daten : null));
  }));
  if (token !== renderToken) return;
  if (planKey(entity) !== planKey(state.entity) || basis !== state.weekStart) return;
  state.around    = { prev: prev || undefined, next: next || undefined };
  state.aroundKey = weekKey(entity, basis);
  /* Auch die Tagesansichten zeigen am Rand Tage der Nachbarwochen. */
  renderGrid();

  /* Wer in eine Richtung weiterwischt, wischt meist noch einmal. Die
     übernächste Woche in dieser Richtung wird deshalb gleich mitgeholt —
     sie liegt dann im Speicher, bevor sie gebraucht wird. Kostet nichts
     zusätzlich: Beim nächsten Wischer wäre sie ohnehin die Nachbarwoche,
     und Gemerktes wird nicht noch einmal angefragt. */
  /* Erst ab dem zweiten Wischer in dieselbe Richtung: Nach einem
     einzelnen Wischer wird oft wieder zurückgegangen, und dann wäre die
     übernächste Woche umsonst geholt. */
  if (!letzteRichtung || folgeWischer < 2) return;
  const weiter = iso(addDays(parseISO(basis), letzteRichtung * 14));
  if (wocheFrisch(wocheGemerkt(entity, weiter))) return;
  loadWeekQuiet(entity, weiter, token).catch(() => null);
}

/* In welche Richtung zuletzt gewischt wurde und wie oft hintereinander. */
let letzteRichtung = 0, folgeWischer = 0;

function gotoWeek(delta){
  state.weekStart = iso(addDays(parseISO(state.weekStart), delta * 7));
  const mo = parseISO(state.weekStart);
  hint(`KW ${isoWeek(mo)} · ${mo.getDate()}.${mo.getMonth()+1}.`);
  refresh();
}

let hintTimer;
function hint(text){
  const el = $('#weekHint');
  el.textContent = text;
  el.classList.add('is-on');
  clearTimeout(hintTimer);
  hintTimer = setTimeout(() => el.classList.remove('is-on'), 900);
}

/* =====================================================================
   Wischen zwischen Wochen und Tagen
   ---------------------------------------------------------------------
   Kopfzeile und Raster liegen als Streifen über drei Wochen vor. Beim
   Wischen wandert der ganze Streifen mit dem Finger, die Nachbarwoche ist
   also schon sichtbar, bevor man loslässt.
   ===================================================================== */
/* WISCHRICHTUNG-BEGINN (wird von den Tests ausgeführt) */
/* Richtung einer Geste, sobald der Finger sich weit genug bewegt hat:
   null = noch offen, 'x' = waagerecht (Woche/Tag blättern, senkrechtes
   Rollen bis zum Loslassen gesperrt), 'y' = senkrecht (Plan rollt, kein
   Wochenwechsel). Waagerecht zählt nur eine deutlich waagerechte Geste —
   schräg nach unten bleibt Rollen. Einmal entschieden, gilt die Richtung
   bis zum Ende der Geste. */
const WISCH_SCHWELLE = 8;
function wischRichtung(mx, my){
  const ax = Math.abs(mx), ay = Math.abs(my);
  if (ax < WISCH_SCHWELLE && ay < WISCH_SCHWELLE) return null;
  return ax > ay * 1.2 ? 'x' : 'y';
}
window.UntPlanWischen = { richtung: wischRichtung, schwelle: WISCH_SCHWELLE };
/* WISCHRICHTUNG-ENDE */
(() => {
  const area = $('#gridScroll');
  /* Eine Woche ist so breit wie der Streifen selbst — nicht wie die ganze
     Fläche: links steht noch die Zeitleiste. Wer hier die Flächenbreite
     nimmt, landet am Ende der Bewegung ein Stück zu weit und muss
     zurückspringen. */
  const WIDTH = () => {
    const st = $('#colStrip');
    return (st && st.clientWidth) || area.clientWidth || 1;
  };

  let x0 = 0, y0 = 0, t0 = 0, dx = 0, lock = null, active = false, busy = false, greifend = false;
  /* Wo der Finger stand, als die Richtung feststand. Ohne diesen Wert
     springt der Streifen im Augenblick des Einrastens um die acht Pixel
     der Erkennungsschwelle — bei einer schnellen Bewegung fällt das nicht
     auf, bei einer ganz langsamen ist es ein deutlicher Ruck. Gezogen wird
     deshalb ab dem Punkt, an dem die Richtung erkannt wurde, nicht ab dem
     Punkt, an dem der Finger aufsetzte. */
  let ankerX = 0;
  /* Höchstens ein Schreibvorgang je Bild. Ein Finger liefert auf manchen
     Geräten mehr Ereignisse, als der Bildschirm Bilder zeigt; jedes davon
     einzeln umzusetzen kostet nur Rechenzeit und macht die Bewegung nicht
     feiner. */
  let frame = 0, offen = null;
  /* Die letzten Fingerpositionen mit Zeit — daraus ergibt sich beim
     Loslassen die Geschwindigkeit des letzten Augenblicks, nicht der
     Durchschnitt über die ganze Geste. */
  let proben = [];

  const strip = () => $('#colStrip');
  const head  = () => $('#headStrip');

  /* Beide Streifen — Datumsleiste und Raster — werden um denselben
     Pixelwert verschoben, und zwar um die Breite des Rasterstreifens.

     Vorher stand dort eine Prozentangabe, die jeder Streifen auf seine
     eigene Breite umrechnete. In der Wochenansicht ging das gut, weil
     beide gleich breit sind; in der Tagesansicht ist der Rasterstreifen
     drei Punkte schmaler (rechts bleibt ein Rand frei). Die Datumsleiste
     lief dadurch je Schritt drei Punkte weiter als der Plan darunter —
     sichtbar als Vorauseilen beim Wischen. */
  let breite = 0;

  const setzen = t => {
    const st = strip(), h = head();
    if (st) st.style.transform = t;
    if (h)  h.style.transform  = t;
  };

  /* Ruhelage: genau eine Streifenbreite nach links. */
  let lage = 0, basis = 0;          /* aktuelle Verschiebung; Startwert der Geste */
  const setShift = px => { lage = px; setzen(`translate3d(${-WIDTH() + px}px, 0, 0)`); };

  /* Gebündelt schreiben: Der letzte Wert vor dem nächsten Bild gewinnt. */
  const shiftBald = px => {
    offen = px;
    if (frame) return;
    frame = requestAnimationFrame(() => {
      frame = 0;
      if (offen !== null && active && lock === 'x') setShift(offen);
      offen = null;
    });
  };

  /* Ruhelage. Wichtig: die Prozentangabe wieder setzen, nicht die
     Verschiebung löschen — ohne Transform stünde der Streifen eine ganze
     Woche zu weit links. */
  const resetShift = () => {
    const w = WIDTH();
    [strip(), head()].forEach(el => {
      if (!el) return;
      el.classList.add('is-dragging');          /* ohne Übergang umsetzen */
      el.style.transitionDuration = '';
      el.style.transform = `translate3d(${-w}px, 0, 0)`;
      void el.offsetWidth;
      el.classList.remove('is-dragging');
    });
  };

  const greifen = an => {
    [strip(), head()].forEach(el => el && el.classList.toggle('is-dragging', an));
  };

  /* Senkrechtes Rollen während einer waagerechten Geste sperren.
     Hauptweg ist preventDefault im touchmove; falls ein Browser das
     Ereignis nicht mehr abbrechen lässt (das Rollen lief schon), hält
     der scroll-Wächter die Rollposition fest, auch im Nachlauf kurz
     nach dem Loslassen. Die Klasse nimmt dem Raster zusätzlich das
     Überlaufen, damit Safari keinen Schwung mehr aufbaut. */
  let sperrTop = 0, sperrBis = 0, gesperrt = false;
  const rollSperre = an => {
    gesperrt = an;
    if (an){ sperrTop = area.scrollTop; sperrBis = 0; }
    else sperrBis = performance.now() + 450;
    area.classList.toggle('is-wischsperre', an);
  };
  area.addEventListener('scroll', () => {
    if ((gesperrt || performance.now() < sperrBis) && area.scrollTop !== sperrTop)
      area.scrollTop = sperrTop;
  }, { passive:true });
  window.ttRollSperre = () => gesperrt;

  area.addEventListener('touchstart', e => {
    if (busy || e.touches.length !== 1) return;
    sperrBis = 0;
    /* In einer rollbaren Lehrerleiste wischt man die Leiste, nicht die Woche. */
    const leiste = e.target.closest && e.target.closest('.card__line--scroll');
    if (leiste && leiste.scrollWidth > leiste.clientWidth + 1){ active = false; return; }
    x0 = e.touches[0].clientX; y0 = e.touches[0].clientY;
    t0 = performance.now();
    dx = 0; lock = null; active = true; ankerX = 0; proben = [];
    /* Federt der Streifen gerade zurück, wird er dort aufgefangen, wo er
       steht — kein Sprung in die Ruhelage. */
    basis = 0;
    if (federBild){ cancelAnimationFrame(federBild); federBild = 0; basis = lage; dx = lage; }
    if (frame){ cancelAnimationFrame(frame); frame = 0; }
    offen = null;
    breite = WIDTH();          /* einmal messen, nicht bei jeder Bewegung */
  }, { passive:true });

  area.addEventListener('touchmove', e => {
    if (!active || e.touches.length !== 1) return;
    const mx = e.touches[0].clientX - x0;
    const my = e.touches[0].clientY - y0;

    if (lock === null){
      lock = wischRichtung(mx, my);
      if (lock === null) return;
      /* Ab hier zählt der Weg — der Weg bis zur Erkennung nicht. */
      ankerX = mx;
      t0 = performance.now();
      if (lock === 'x') rollSperre(true);
    }
    if (lock !== 'x') return;
    /* Waagerecht erkannt: Bis der Finger losgelassen wird, rollt das
       Raster nicht mehr senkrecht mit. Der Listener ist dafür bewusst
       nicht passiv — Safari auf dem iPhone beendet das Rollen nur über
       preventDefault, touch-action allein reicht dort nicht. Vor der
       Erkennung wird nichts verhindert: Ein abgefangenes erstes
       touchmove sperrte in manchen Browsern auch das senkrechte Rollen
       einer eindeutig senkrechten Geste. */
    if (e.cancelable) e.preventDefault();
    if (area.scrollTop !== sperrTop) area.scrollTop = sperrTop;

    const weg = mx - ankerX + basis;

    /* Am Rand des Streifens zäher werden. */
    if (!greifend){ greifen(true); greifend = true; }
    dx = Math.abs(weg) > breite
       ? Math.sign(weg) * (breite + (Math.abs(weg) - breite) * 0.3)
       : weg;
    proben.push([performance.now(), dx]);
    if (proben.length > 8) proben.shift();
    shiftBald(dx);
  }, { passive:false });

  /* Nach dem Gleiten still neu aufbauen — der Streifen steht dann schon
     an der richtigen Stelle, es ruckelt nichts. */
  /* Die vorgeladene Nachbarwoche wird nur übernommen, wenn sie zu dem
     Plan und der Woche gehört, von der aus gewischt wurde, und wenn ihre
     eigene Herkunft zur neuen Woche passt. Ohne diese Prüfung landeten
     nach einem Planwechsel die Kacheln des vorherigen Plans in der
     neuen Woche — sie lagen ja schon vorgeladen bereit. */
  const nachbarWoche = (dir, vonWoche, neueWoche) => {
    if (state.aroundKey !== weekKey(state.entity, vonWoche)) return null;
    const w = dir > 0 ? (state.around || {}).next : (state.around || {}).prev;
    if (!w) return null;
    if (w.key && w.key !== weekKey(state.entity, neueWoche)) return null;
    return w;
  };

  const commit = dir => {
    folgeWischer = (dir === letzteRichtung) ? folgeWischer + 1 : 1;
    letzteRichtung = dir;
    const keep = area.scrollTop;
    const vonWoche = state.weekStart;
    greifend = false;
    if (tageAnsicht()){
      const target = tageWeiter(state.day, dir * tageAnsicht());
      state.day = target;
      const mon = iso(mondayOf(parseISO(target)));
      if (mon !== state.weekStart){
        state.weekStart = mon;
        state.week = nachbarWoche(dir, vonWoche, mon)
                  || (wocheGemerkt(state.entity, mon) || {}).daten
                  || { byDate:{}, bands:[] };
        nachbarnAusSpeicher();
        renderWeekHead(); renderGrid(); startWennSichtbar();
        area.scrollTop = keep; busy = false;
        spaeterNachladen();
        return;
      }
    } else {
      state.weekStart = iso(addDays(parseISO(state.weekStart), dir * 7));
      /* Die Woche kommt aus dem Streifen oder aus dem Speicher — erst
         wenn beides nichts hergibt, wird geladen. */
      state.week = nachbarWoche(dir, vonWoche, state.weekStart)
                || (wocheGemerkt(state.entity, state.weekStart) || {}).daten
                || { byDate:{}, bands:[] };
      nachbarnAusSpeicher();
    }
    /* Der Neuaufbau kostet ein paar Millisekunden — er schreibt den ganzen
       Streifen neu. Liefe er in demselben Bild wie das Ende der
       Gleitbewegung, fiele genau dort ein Bild aus, und das sieht man als
       Ruckler. Zwei Bilder später steht die Bewegung still, und niemandem
       fällt auf, dass dazwischen gearbeitet wird. */
    requestAnimationFrame(() => requestAnimationFrame(() => {
      renderDayBar(); renderWeekHead(); renderGrid(); startWennSichtbar();
      area.scrollTop = keep;
      busy = false;
      spaeterNachladen();
    }));
  };

  /* Die Startseite liegt hinter dem Stundenplan — sie neu zu bauen kostet
     nur Zeit, solange sie niemand sieht. */
  const startWennSichtbar = () => {
    const el = $('#screenStart');
    if (el && !el.hidden && el.offsetParent !== null) renderStart();
  };

  /* Lag die Woche schon bereit, steht sie sofort richtig da — dann werden
     nur noch still die nächsten Nachbarwochen geholt. Fehlt sie, wird
     sofort nachgeladen, und der Balken unter der Kopfzeile zeigt das. */
  const spaeterNachladen = () => {
    const da = state.week && Object.keys(state.week.byDate || {}).length > 0;
    if (!da){ refresh(); return; }
    const still = window.requestIdleCallback || (f => setTimeout(f, 200));
    still(() => preloadNeighbours(renderToken), { timeout: 800 });
  };

  /* Übergangsdauer für das Nachlaufen dynamisch setzen — schneller Flick =
     schneller einrasten, wie in einer nativen App. */


  /* Feder wie beim Blättern in iOS: Der Streifen behält beim Loslassen
     die Geschwindigkeit des Fingers und läuft gedämpft ins Ziel — ohne
     festen Zeitplan, deshalb ohne Knick zwischen Ziehen und Gleiten.
     Geschrieben wird ausschließlich transform, Bild für Bild. */
  let federBild = 0;
  const federn = (von, ziel, v0, fertig) => {
    cancelAnimationFrame(federBild);
    if (window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches){
      setShift(ziel); federBild = 0; fertig && fertig(); return;
    }
    greifen(true);
    const steif = 340, daempf = 2 * Math.sqrt(steif) * 0.94;   /* knapp unter kritisch */
    let x = von - ziel, v = v0 * 1000, zuletzt = performance.now();
    const schritt = jetzt => {
      let dt = Math.min(0.05, (jetzt - zuletzt) / 1000); zuletzt = jetzt;
      /* In kleinen Teilschritten rechnen — bei 60 und bei 120 Bildern je
         Sekunde dieselbe Bahn. */
      while (dt > 0){
        const h = Math.min(dt, 1 / 240);
        v += (-steif * x - daempf * v) * h;
        x += v * h;
        dt -= h;
      }
      if (Math.abs(x) < 0.4 && Math.abs(v) < 12){
        setShift(ziel);
        federBild = 0;
        fertig && fertig();
        return;
      }
      setShift(ziel + x);
      federBild = requestAnimationFrame(schritt);
    };
    federBild = requestAnimationFrame(schritt);
  };

  /* Geschwindigkeit der letzten rund 80 ms in Pixeln je Millisekunde. */
  const tempo = () => {
    if (proben.length < 2) return 0;
    const [t1, x1] = proben[proben.length - 1];
    let i = proben.length - 2;
    while (i > 0 && t1 - proben[i][0] < 80) i--;
    const [t0p, x0p] = proben[i];
    /* Wer vor dem Loslassen innehält, hat keinen Schwung mehr. */
    if (performance.now() - t1 > 90) return 0;
    return (x1 - x0p) / Math.max(8, t1 - t0p);
  };

  /* Eine Seite weiter: dieselbe Feder für Wischen, Pfeiltasten und
     Pfeilknöpfe. dir > 0 = vorwärts (Inhalt wandert nach links). */
  const blaettern = (dir, von, v0) => {
    buzz();
    busy = true;
    const w = WIDTH();
    let done = false;
    federn(von, dir > 0 ? -w : w, v0, () => {
      if (done) return; done = true;
      requestAnimationFrame(() => commit(dir));
    });
  };
  window.ttBlaettern = dir => {
    if (busy || active) return false;
    if (!strip()) return false;
    breite = WIDTH();
    blaettern(dir, 0, 0);
    return true;
  };

  const release = () => {
    if (gesperrt) rollSperre(false);
    if (!active) return;
    active = false;
    /* Ein noch offener Schreibvorgang würde nach dem Loslassen dazwischen-
       funken und die Gleitbewegung an den Anfang zurückwerfen. */
    if (frame){ cancelAnimationFrame(frame); frame = 0; }
    if (offen !== null){ setShift(offen); offen = null; }
    if (lock !== 'x'){
      lock = null;
      /* Aufgefangen, aber nicht weitergezogen: zurück in die Ruhelage. */
      if (basis) federn(lage, 0, 0, () => { if (!active){ resetShift(); greifend = false; } });
      return;
    }
    lock = null;

    const w = WIDTH();
    const v = tempo();                                   /* px je ms */
    /* Wohin der Streifen mit diesem Schwung von selbst liefe — wie die
       Seitenwahl von iOS: halbe Breite oder ein deutlicher Wurf. */
    const ziel = dx + v * 180;
    const wurf = Math.abs(v) > 0.35 && Math.sign(v) === Math.sign(dx);
    const far = Math.abs(ziel) > w * 0.5 || (wurf && Math.abs(dx) > 12);

    if (!far){
      /* Zurückfedern, mit dem Restschwung. */
      federn(dx, 0, v, () => { if (!active){ resetShift(); greifend = false; } });
      return;
    }
    const dir = dx < 0 ? 1 : -1;
    /* Schwung in Gegenrichtung wird nicht mitgenommen. */
    blaettern(dir, dx, Math.sign(v) === -dir ? v : 0);
  };

  area.addEventListener('touchend', release, { passive:true });
  area.addEventListener('touchcancel', release, { passive:true });
})();

/* Blättern ohne Finger: Pfeiltasten und waagerechtes Wischen auf dem
   Trackpad (iPad mit Tastatur, Rechner) nehmen dieselbe Feder wie das
   Wischen. In der Tagesansicht ein Tag, sonst eine Woche. */
function planBlaettern(dir){
  if (state.screen !== 'plan') return;
  if (window.ttBlaettern && window.ttBlaettern(dir)) return;
  if (tageAnsicht()) stepDay(dir); else gotoWeek(dir);
}
document.addEventListener('keydown', e => {
  if (state.screen !== 'plan' || e.altKey || e.metaKey || e.ctrlKey) return;
  if (e.target.closest && e.target.closest('input, textarea, [contenteditable]')) return;
  if (document.querySelector('.sheet:not([hidden])')) return;
  if (e.key === 'ArrowLeft'){ e.preventDefault(); planBlaettern(-1); }
  if (e.key === 'ArrowRight'){ e.preventDefault(); planBlaettern(1); }
});
(() => {
  let summe = 0, sperre = 0;
  $('#gridScroll').addEventListener('wheel', e => {
    if (Math.abs(e.deltaX) <= Math.abs(e.deltaY)) return;
    e.preventDefault();
    const jetzt = performance.now();
    if (jetzt < sperre) return;                 /* Nachlauf des Trackpads */
    summe += e.deltaX;
    if (Math.abs(summe) > 60){
      planBlaettern(summe > 0 ? 1 : -1);
      summe = 0; sperre = jetzt + 600;
    }
  }, { passive:false });
})();
$('#dayPrev').addEventListener('click', e => { e.stopImmediatePropagation(); planBlaettern(-1); }, true);
$('#dayNext').addEventListener('click', e => { e.stopImmediatePropagation(); planBlaettern(1); }, true);

/* Kurzes Rückmelden beim Antippen von Kacheln und Reitern */
document.addEventListener('touchstart', e => {
  if (!navigator.vibrate) return;
  if (e.target.closest('.card, .tab, .parcount, .picker-item, .pk__row'))
    try { navigator.vibrate(5); } catch (err){}
}, { passive:true });

/* =====================================================================
   Screens / Tabbar
   ===================================================================== */
/* Übergang wie in einer iOS-Navigation. Die Klasse fällt nach der
   Bewegung wieder weg, damit keine Transform-Ebene stehen bleibt. */
function iosAnim(el, cls){
  if (!el) return;
  el.classList.remove('ios-push', 'ios-pop', 'ios-leave');
  void el.offsetWidth;
  el.classList.add(cls);
  const blatt = el.closest('.sheet');
  if (blatt) blatt.classList.add('ios-clip');
  let fertig = false;
  const ende = e => {
    if (e && e.target !== el) return;
    if (fertig) return; fertig = true;
    el.classList.remove(cls);
    el.removeEventListener('animationend', ende);
    if (blatt && !blatt.querySelector('.ios-push, .ios-pop')) blatt.classList.remove('ios-clip');
  };
  el.addEventListener('animationend', ende);
  setTimeout(ende, 700);           /* falls das Ereignis ausbleibt */
}

/* Unteransichten in Blättern und Seiten schieben sich von rechts herein;
   wird eine geschlossen, gleitet die Ansicht darunter aus der Tiefe nach. */
(() => {
  const paare = [['#detailSub', '#detailBody'], ['#detailAbsList', '#detailSub'], ['#detailAbs', null], ['#detailForm', null],
                 ['#msgRead', '#msgList'], ['#pickerList', '#pickerRoot']];
  const offen = el => {
    const blatt = el.closest('.sheet');
    if (blatt) return blatt.classList.contains('is-open') && !blatt.hidden;
    const seite = el.closest('.screen');
    return !!seite && seite.classList.contains('is-active');
  };
  const mo = new MutationObserver(liste => liste.forEach(m => {
    const el = m.target;
    const p = paare.find(([sel]) => el.matches(sel));
    if (!p) return;
    const warVerborgen = m.oldValue !== null;
    if (warVerborgen && !el.hidden){ if (offen(el)) iosAnim(el, 'ios-push'); }
    else if (!warVerborgen && el.hidden && p[1]){
      const b = $(p[1]);
      if (b && !b.hidden && offen(el)) iosAnim(b, 'ios-pop');
    }
  }));
  paare.forEach(([sel]) => {
    const el = $(sel);
    if (el) mo.observe(el, { attributes:true, attributeFilter:['hidden'], attributeOldValue:true });
  });
})();

function showScreen(name){
  const vorher = state.screen;
  state.screen = name;
  /* Listen (Termine, Abwesenheiten …) sind Unterseiten der Startseite:
     hinein von rechts, zurück wieder nach rechts hinaus. */
  if (name === 'list' && vorher === 'start') iosAnim($('#screenList'), 'ios-push');
  if (vorher === 'list' && name === 'start'){
    iosAnim($('#screenList'), 'ios-leave');
    iosAnim($('#screenStart'), 'ios-pop');
  }
  /* Schüler*innen haben nur ihren eigenen Plan — dann führt die Auswahl
     ins Leere und die Knöpfe dafür verschwinden. */
  /* Schüler*innen dürfen Klassen- und Raumpläne ansehen; die Auswahl
     bleibt also. Verborgen wird alles, wofür ihnen das Recht fehlt. */
  if (state.isStudent) rechteAnwenden();
  const map = { plan:'#screenPlan', start:'#screenStart', msg:'#screenMsg',
                profile:'#screenProfile', list:'#screenList' };
  Object.entries(map).forEach(([k, sel]) => $(sel).classList.toggle('is-active', k === name));
  /* Jetzt erst hat der Plan Ausmaße. Was im Verborgenen gezeichnet wurde,
     konnte nicht vermessen werden — hier wird es nachgeholt, sobald der
     Browser die Seite tatsächlich aufgebaut hat. */
  if (name === 'plan') requestAnimationFrame(feinKuerzung);
  $$('.tab').forEach(t => {
    /* Die Listen gehören zur Startseite — dort bleibt der Reiter stehen. */
    const on = t.dataset.screen === (name === 'list' ? 'start' : name);
    t.classList.toggle('is-active', on);
    t.setAttribute('aria-selected', on ? 'true' : 'false');
  });
  /* Die Kopfleiste bleibt auf allen Seiten stehen — sonst käme man vom
     Start aus nicht an den Profilwechsel. Nur die Planauswahl in der Mitte
     verschwindet dort, weil sie ohne Plan nichts auswählt. */
  $('#topbar').style.display = (name === 'list') ? 'none' : '';
  /* Der Grund der Kopfleiste folgt der Seite, damit oben keine Kante
     entsteht. (:has kann noch nicht jeder Browser.) */
  document.body.classList.toggle('has-plain-top', name !== 'plan' && name !== 'msg');
  /* Die Startseite hatte einen eigenen Knopf zum Planwechsel — mit der
     durchgehenden Kopfleiste stünde er doppelt da. */
  const zweit = $('#btnSwitchEntity2');
  if (zweit) zweit.hidden = true;
  const titel = $('#btnEntityPicker');
  if (titel) titel.style.visibility = (name === 'plan') ? '' : 'hidden';
  const akt = $('#topbarActions');
  if (akt) akt.style.visibility = (name === 'plan') ? '' : 'hidden';
  /* Der Kontoknopf wandert mit: Auf dem Mitteilungsbildschirm steht er in
     dessen eigener Kopfzeile, sonst in der Leiste der App. So sitzen Titel
     und Knöpfe dort auf einer Höhe, und über ihnen bleibt es einfarbig. */
  const konto = $('#btnSwitchEntity');
  if (name === 'msg'){
    document.body.classList.add('is-msgtop');
    $('#msgHeadLeft').appendChild(konto);
    renderMessages();
  } else if (document.body.classList.contains('is-msgtop')) {
    document.body.classList.remove('is-msgtop');
    konto.hidden = false;
    $('.topbar').prepend(konto);
  }
}
$$('.tab').forEach(t => t.addEventListener('click', () => {
  /* Steht man schon auf dem Stundenplan, führt ein zweiter Tipp zurück zur
     laufenden Woche — wie das Antippen des aktiven Reiters in einer App. */
  /* Die gewählte Ansicht (Tag, 2 Tage, 3 Tage, Woche) bleibt; nur der
     Zeitraum springt auf heute. Steht man schon dort, wird der Plan
     aufgefrischt. */
  if (t.dataset.screen === 'plan' && state.screen === 'plan'){
    const heute = schultag(iso(new Date()));
    const mon = iso(mondayOf(tageAnsicht() ? parseISO(heute) : new Date()));
    const schonDa = mon === state.weekStart && (!tageAnsicht() || state.day === heute);
    if (tageAnsicht()) state.day = heute;
    if (schonDa){ datenNeuLaden().then(scrollToNow); return; }
    if (mon !== state.weekStart){
      state.weekStart = mon; state.around = {};
      refresh().then(scrollToNow);
    } else {
      renderDayBar(); renderWeekHead(); renderGrid(); scrollToNow();
    }
    return;
  }
  showScreen(t.dataset.screen);
  if (t.dataset.screen === 'start') startPlanLaden();
}));

/* =====================================================================
   Startbildschirm
   ===================================================================== */
/* Die Karte auf der Startseite zeigt den eigenen Plan — auch wenn im
   Stundenplan gerade eine andere Klasse ausgewählt ist. */
let startWeek = null, startFor = null;

async function startPlanLaden(){
  const eigen = state.entities.find(e => e.type === 'mine') || state.entities[0];
  if (!eigen) return;
  /* „In den nächsten 7 Tagen“: die laufende und die folgende Woche, damit
     am Wochenende auch der Montag und kommende Ferien zu sehen sind. */
  const mo = iso(mondayOf(new Date()));
  const mo2 = iso(addDays(parseISO(mo), 7));
  const k = weekKey(eigen, mo);
  if (startFor === k) return;
  startFor = k;
  try {
    const w = await loadWeekShared(eigen, mo);
    /* Zwischenzeitlich kann eine andere Woche angefordert worden sein. */
    if (startFor !== k) return;
    if (w.key && w.key !== k) return;
    let w2 = null;
    try { w2 = await loadWeekShared(eigen, mo2); } catch (e){ w2 = null; }
    if (startFor !== k) return;
    startWeek = Object.assign({}, w, {
      byDate: Object.assign({}, (w2 && !w2.gesperrt && w2.byDate) || {}, w.byDate || {}),
      thisWeek: w.byDate || {}
    });
    if (state.screen === 'start') renderStart();
  } catch (e){ startFor = null; /* ohne Verbindung bleibt die Karte, wie sie ist */ }
}

function renderStart(){
  const now = new Date();
  const WD = ['Sonntag','Montag','Dienstag','Mittwoch','Donnerstag','Freitag','Samstag'];

  $('#startSchool').textContent = (BOOT && BOOT.school) || 'Stundenplan';
  $('#startWeekday').textContent = WD[now.getDay()];
  $('#startDayNum').textContent  = now.getDate();
  $('#startMonth').textContent   = MONTHS[now.getMonth()];

  /* Marke unter dem Datum: an unterrichtsfreien Tagen das Datum in Blau. */
  /* Die Startseite zeigt ausschließlich den eigenen Plan. Ist er noch
     nicht geladen, darf ersatzweise nur dann die laufende Ansicht
     einspringen, wenn das ohnehin der eigene Plan ist — sonst standen
     hier die Stunden einer fremden Klasse unter der Überschrift
     „Mein Stundenplan“. */
  const eigen  = state.entities.find(e => e.type === 'mine') || state.entities[0];
  const istIch = planKey(state.entity) === planKey(eigen);
  const quelle = (startWeek && startWeek.byDate)
              || (istIch ? (state.allByDate || state.week.byDate) : {});
  const todayCards = quelle[iso(now)] || [];
  const bis7 = iso(addDays(now, 7));
  const free = todayCards.some(c => c.holiday);
  const tag = $('#startTag');
  tag.hidden = !free;
  if (free) tag.textContent = `${now.getDate()}.${now.getMonth() + 1}.`;

  /* Die nächsten Stunden, nach Tagen gruppiert. */
  const all = [];
  Object.entries(quelle).forEach(([d, cards]) =>
    (cards || []).forEach((c, i) => { c._uid = i; all.push(c); }));
  all.sort((a, b) => a.date.localeCompare(b.date) || a.lesson - b.lesson);

  /* Die nächsten vier Stunden ab jetzt, Ferien und Felder ausgenommen. */
  const upcoming = all.filter(c => {
    const e = cardEndDate(c);
    return e && e >= now && c.date < bis7 && !c.holiday && !c.overlay
        && c.blockType !== 'holiday' && c.blockType !== 'standby';
  }).slice(0, 4);

  /* Ferien und Feiertage der nächsten 7 Tage (je Zeitraum einmal): Liegen
     sie vor der nächsten Stunde, stehen sie in der Karte — ohne Stunden
     als ganze Fläche wie „Genieße die Zeit!“, sonst als Zeile davor. */
  const ferien = [];
  Object.keys(quelle).sort().forEach(d => {
    if (d < iso(now) || d >= bis7) return;
    const f = (quelle[d] || []).find(c => c.holiday || c.blockType === 'holiday');
    if (!f) return;
    const name = String(f.holidayName || '').replace(/\u00ad/g, '') || f.subject || 'Ferien';
    const letzte = ferien[ferien.length - 1];
    if (letzte && letzte.name === name && letzte.range === (f.holidayRange || '')) return;
    ferien.push({ date:d, name, art:f.subject || 'Ferien', range:f.holidayRange || '' });
  });
  const ferienVorher = ferien.filter(f => !upcoming.length || f.date <= upcoming[0].date);
  const ferienText = f => f.range ? (f.name !== f.art ? `${f.art} ${f.range}` : f.range)
                                 : `${WD[parseISO(f.date).getDay()]}, ${parseISO(f.date).getDate()}. ${MONTHS[parseISO(f.date).getMonth()]}`;

  const box = $('#startNext');
  /* Liegt die Woche außerhalb des Plan-Fensters der Rolle, gibt es nichts
     zu genießen — wie in der iOS-App steht dann der Sperrsatz. */
  const gesperrt = !!((startWeek && startWeek.byDate === quelle) ? startWeek.gesperrt
                     : (istIch && state.week && state.week.gesperrt));
  if (!upcoming.length && gesperrt){
    box.innerHTML = `<div class="nextnone nextnone--sperre">
      <span class="sperre__kreis" aria-hidden="true">${SPERRE_SCHLOSS}</span>
      <b>Keine Stunden</b>
      <span>${SPERRE_TITEL}</span></div>`;
    return;
  }
  if (!upcoming.length && ferienVorher.length){
    const f = ferienVorher[0];
    box.innerHTML = `<div class="nextnone nextnone--ferien">
      <svg viewBox="0 0 24 24" class="ico"><rect x="3.5" y="5" width="17" height="15.5" rx="2.4"/>
        <path d="M3.5 9.6h17M8 3.4v3.4M16 3.4v3.4"/>
        <path d="m12 12.4 1 2 2.2.3-1.6 1.5.4 2.2-2-1-2 1 .4-2.2-1.6-1.5 2.2-.3z"/></svg>
      <b>${esc(f.name)}</b>
      <span>${esc(ferienText(f))}</span>
      ${ferienVorher.slice(1).map(g => `<span>${esc(g.name)} · ${esc(ferienText(g))}</span>`).join('')}</div>`;
    return;
  }
  if (!upcoming.length){
    box.innerHTML = `<div class="nextnone">
      <svg viewBox="0 0 24 24" class="ico"><rect x="3.5" y="5" width="17" height="15.5" rx="2.4"/>
        <path d="M3.5 9.6h17M8 3.4v3.4M16 3.4v3.4"/>
        <path d="m12 12.4 1 2 2.2.3-1.6 1.5.4 2.2-2-1-2 1 .4-2.2-1.6-1.5 2.2-.3z"/></svg>
      <b>Genieße die Zeit!</b>
      <span>In den nächsten 7 Tagen ist kein Unterricht geplant.</span></div>`;
    return;
  }

  let last = '';
  /* Ferienzeile vor den Stunden: blauer Balken, Name und Zeitraum. */
  const ferienZeile = f => `<div class="nextrow nextrow--ferien">
      <span class="nextrow__bar"></span>
      <span class="nextrow__main"><span>${esc(f.name)}</span><span>${esc(ferienText(f))}</span></span>
      <span class="nextrow__times"></span>
    </div>`;
  /* Ferien nach den nächsten Stunden (innerhalb der 7 Tage) stehen darunter. */
  const ferienNachher = ferien.filter(f => !ferienVorher.includes(f));
  box.innerHTML = ferienVorher.map(ferienZeile).join('') + upcoming.map(c => {
    const dd = parseISO(c.date);
    /* Die Karte zeigt immer den eigenen Plan — der Zusatz sagt das auch,
       selbst wenn im Stundenplan gerade eine fremde Klasse ausgewählt ist. */
    const head = c.date !== last
      ? `<div class="nextday">${dd.getDate()}. ${MONTHS_SHORT[dd.getMonth()]}
           <span>Mein Stundenplan</span></div>` : '';
    last = c.date;
    const t1 = (state.times[c.lesson] || {}).start || '';
    const t2 = (state.times[Math.min(c.lesson + c.span - 1, 12)] || {}).end || '';
    const off = c.blockType === 'cancelled';
    const lines = [c.class || c.teacher, c.subject, c.room].filter(Boolean);
    return head + `<button class="nextrow ${off ? 'is-off' : ''}"
        data-date="${c.date}" data-lesson="${c.lesson}" data-idx="${c._uid}">
      <span class="nextrow__bar"></span>
      <span class="nextrow__main">${lines.map(l => `<span>${esc(l)}</span>`).join('')}</span>
      <span class="nextrow__times"><span>${t1}</span><span>${t2}</span></span>
    </button>`;
  }).join('') + ferienNachher.map(ferienZeile).join('');

  $$('#startNext .nextrow').forEach(el => el.addEventListener('click', () => {
    showScreen('plan');
    openDetail(el.dataset.date, +el.dataset.lesson, +(el.dataset.idx || 0), quelle);
  }));
}

/* Infos zum Tag oben auf der Startseite: Karte mit farbigem Kopf (Nachricht blau mit Megafon, Veranstaltung grün),
   Kennzeichen „neu“ und Text – mehrere Karten werden waagrecht gewischt. */
function newsZeichnen(liste){
  const box = $('#startNews');
  if (!box) return;
  box.hidden = !liste.length;
  const MEGAFON = '<svg viewBox="0 0 24 24" class="ico"><path d="M3.8 10v4h3l7.4 4V6L6.8 10z"/><path d="M6.8 14l1.3 4.6h2.3L9.4 14.4M17.6 9.4a3.6 3.6 0 0 1 0 5.2"/></svg>';
  const GRUPPE = '<svg viewBox="0 0 24 24" class="ico"><circle cx="8.5" cy="8.4" r="2.6"/><circle cx="15.8" cy="8.4" r="2.6"/><path d="M3.8 18.4c.5-3 2.4-4.6 4.7-4.6s4.2 1.6 4.7 4.6M11.4 14.6c.9-.6 2.2-.8 4.4-.8 2.3 0 4.2 1.6 4.6 4.6"/></svg>';
  box.innerHTML = liste.map(n => `<article class="newscard newscard--${n.kind === 'event' ? 'event' : 'news'}">
      <header class="newscard__head">${n.kind === 'event' ? GRUPPE : MEGAFON}<span class="newscard__neu">neu</span></header>
      <div class="newscard__body"><b>${esc(n.title || '')}</b><p>${esc(n.text || '')}</p></div>
    </article>`).join('');
}

function tile(c){
  const t1 = (state.times[c.lesson] || {}).start || '';
  const dd = parseISO(c.date);
  const kind = viewKind(state.entity);
  const lines = cardLines(c, kind);
  const flag = { change:['flag--change','Änderung'], cancelled:['flag--cancel','Entfällt'],
                 klausur:['flag--exam','Klausur'], event:['flag--event','Termin'],
                 'class-absent':['flag--cancel','Abwesend'] }[c.blockType];
  return `<button class="tile" data-date="${c.date}" data-lesson="${c.lesson}" data-idx="${c._uid ?? 0}">
    <span class="tile__time">${t1}<small>${WD_SHORT[(dd.getDay()+6)%7]} ${dd.getDate()}.</small></span>
    <span class="tile__main"><b>${esc(lines[0]?.text || '')} · ${esc(c.subject || '')}</b>
      <span>${esc(lines.slice(1).map(l => l.text).join(' · '))}</span></span>
    ${flag ? `<span class="tile__flag ${flag[0]}">${flag[1]}</span>` : '<span></span>'}
  </button>`;
}

/* =====================================================================
   Mitteilungen
   ---------------------------------------------------------------------
   Bedient wird `api/messages` — dahinter stehen die Mitteilungen von UntPlanWeb.
   Posteingang, Gesendet und Entwürfe, Lesen, Antworten, Schreiben und
   Löschen laufen damit über dieselben Tabellen wie am Rechner: Was dort
   gelesen wurde, ist auch hier gelesen.
   ===================================================================== */
const MSG_URL = 'api/messages';
const MSG_FACH = { in:'inbox', out:'sent', draft:'drafts' };
const MSG_FACH_NAME = { in:'Posteingang', out:'Gesendet', draft:'Entwürfe' };
const MSG_TYP_NAME = { kollegium:'Kolleg*innen', individuell:'Individuell' };

let msgBox = 'in';
let msgSuche = '';
let msgOffen = null;          /* gerade gelesene Mitteilung */
let msgEntwurf = 0;           /* Entwurf, der bearbeitet wird */
let msgEmpfaenger = [];       /* gewählte Empfänger {token, label, initials} */
let msgAnhaenge = [];         /* Dateien, die mitgehen */
let msgSucheTimer = 0;
let msgTyp = 'kollegium';     /* Kolleg*innen oder Individuell */
let msgAntwortAuf = 0;        /* Antwort auf diese Mitteilung */
let msgOpt = { receipt:false, noReply:false };
let msgMax = { files:5, mb:7 };

/* Kürzel aus dem Namen: „Scheibel Florian-Fynn" wird zu „SF", ein
   Kürzel wie „NEU" zu „N" (wie in der iOS-App). */
function initialen(name){
  const t = String(name || '').replace(/[^\p{L}\s-]/gu, ' ').trim().split(/[\s-]+/).filter(Boolean);
  if (!t.length) return '?';
  if (t.length === 1) return t[0][0].toUpperCase();
  return (t[0][0] + t[1][0]).toUpperCase();
}

async function msgFetch(action, extra, post){
  const p = new URLSearchParams(Object.assign({ action }, extra || {}));
  const opt = { credentials:'same-origin' };
  if (post){
    opt.method = 'POST';
    opt.body = post instanceof FormData ? post : new URLSearchParams(post);
  }
  const r = await fetch(MSG_URL + '?' + p, opt);
  const d = await r.json().catch(() => null);
  if (!d || !d.ok) throw new Error((d && d.error) || 'Fehler');
  return d;
}

/* Datum in der Liste: Tag und Monat; im Detail „19.01.26 | 07:24". */
function msgDatum(iso, lang){
  const d = new Date(String(iso || '').replace(' ', 'T'));
  if (isNaN(d)) return '';
  if (lang)
    return ('0' + d.getDate()).slice(-2) + '.' + ('0' + (d.getMonth() + 1)).slice(-2) + '.'
         + String(d.getFullYear()).slice(-2) + '<i class="msgread__trenn">|</i>'
         + ('0' + d.getHours()).slice(-2) + ':' + ('0' + d.getMinutes()).slice(-2);
  return d.getDate() + '. ' + MONTHS_SHORT[d.getMonth()];
}

/* Adressen und Verweise im Text anklickbar machen — sonst stünde der
   Anmeldebogen aus einer Mitteilung nur als Zeichenkette da. */
function msgText(roh){
  return esc(String(roh || ''))
    .replace(/(https?:\/\/[^\s<]+|www\.[^\s<]+)/g,
             m => `<a href="${m.startsWith('http') ? m : 'https://' + m}" target="_blank" rel="noopener">${m}</a>`)
    .replace(/([\w.+-]+@[\w-]+\.[\w.]+)/g, '<a href="mailto:$1">$1</a>')
    .replace(/\n/g, '<br>');
}

const MSG_CLIP = '<svg viewBox="0 0 24 24" class="ico msgrow__clip" aria-label="Anhang"><path d="M20 11.5 12 19.4a4.6 4.6 0 0 1-6.5-6.5l8.4-8.3a3 3 0 1 1 4.3 4.3l-8.2 8.2a1.5 1.5 0 0 1-2.1-2.1l7.6-7.6"/></svg>';
const MSG_PDF = '<svg viewBox="0 0 24 24" class="ico msgfile__ico" aria-hidden="true"><rect x="4.6" y="3.4" width="14.8" height="17.2" rx="2"/><path d="M8.4 16.6c2.2-1.2 4.6-4.4 3.8-6.6-.5-1.4-2-.4-1.3 1.9.8 2.6 3.3 4.4 4.8 4.3 1.3-.1.7-1.6-1.4-1.4-2.4.2-4.6 1.4-5.9 1.8"/></svg>';
const MSG_DATEI = '<svg viewBox="0 0 24 24" class="ico msgfile__ico" aria-hidden="true"><path d="M7 3.4h6.8l4.6 4.6v11a1.6 1.6 0 0 1-1.6 1.6H7a1.6 1.6 0 0 1-1.6-1.6V5A1.6 1.6 0 0 1 7 3.4z"/><path d="M13.6 3.6V8.2h4.6"/></svg>';

/* ------------------------------------------------------------------
   Liste — Posteingang wie in der iOS-App: Avatar mit Initiale, Absender,
   Betreff, Vorschau, Büroklammer bei Anhängen
   ------------------------------------------------------------------ */
function msgZeile(m){
  /* Im Posteingang zählt, wer geschrieben hat; in Gesendet und Entwürfen,
     an wen es geht. */
  const wer = msgBox === 'in'
    ? (m.sender || '?')
    : (m.recipientsText || (m.recipients || []).map(r => r.label).join(', ') || 'Ohne Empfänger');
  const vorschau = String(m.body || '').replace(/\s+/g, ' ').trim();
  return `<button class="msgrow ${m.read === false ? 'is-unread' : ''}" data-msg="${m.id}">
      <span class="msgrow__ava">${esc(msgBox === 'in' && m.initials ? m.initials : initialen(wer))}</span>
      <span class="msgrow__main">
        <b>${esc(wer)}</b>
        <span>${esc(m.subject || 'Ohne Betreff')}</span>
        <em>${esc(vorschau.slice(0, 160))}</em>
      </span>
      <span class="msgrow__side">
        <span class="msgrow__date">${esc(msgDatum(m.sentAt || m.updatedAt))}</span>
        ${m.attachmentCount ? MSG_CLIP : ''}
      </span>
    </button>`;
}

const MSG_LEER = {
  in:   ['Keine Mitteilungen', 'Derzeit gibt es hier noch nichts'],
  out:  ['Nichts gesendet', 'Was Sie versenden, steht danach hier'],
  draft:['Keine Entwürfe', 'Angefangene Mitteilungen liegen hier']
};

function msgLeer(){
  const [t1, t2] = MSG_LEER[msgBox];
  return `<div class="msgempty">
      <span class="msgempty__ico">
        <svg viewBox="0 0 24 24"><rect x="3.6" y="5.6" width="16.8" height="12.8" rx="2"/><path d="m4.4 6.6 7.6 6 7.6-6"/></svg>
      </span>
      <b>${t1}</b><span>${t2}</span>
    </div>`;
}

async function renderMessages(){
  msgLesenSchliessen();
  msgPopsZu();
  $('#msgBoxBtn').textContent = MSG_FACH_NAME[msgBox];
  $$('#msgBoxPop [data-box]').forEach(b => b.classList.toggle('is-on', b.dataset.box === msgBox));
  const ziel = $('#msgList');
  let d;
  try {
    d = await msgFetch('list', { box: MSG_FACH[msgBox] || 'inbox', q: msgSuche });
  } catch (e) {
    ziel.innerHTML = '<div class="msgempty"><b>Nicht erreichbar</b><span>Die Mitteilungen ließen sich nicht laden</span></div>';
    return;
  }
  ziel.innerHTML = d.items.length ? d.items.map(msgZeile).join('') : msgLeer();
  $$('#msgList [data-msg]', ziel).forEach(b =>
    b.addEventListener('click', () => msgOeffnen(+b.dataset.msg)));

  if (msgBox === 'in') msgPunkt(d.items);
  else msgPunktHolen();
}

function msgPunkt(items){ $('#msgDot').hidden = !items.some(m => m.read === false); }
async function msgPunktHolen(){
  try { msgPunkt((await msgFetch('list', { box:'inbox' })).items); } catch (e) {}
}

/* Popover (Verfassen, Ordner) schließen. */
function msgPopsZu(){
  $('#msgNewPop').hidden = true;
  $('#msgBoxPop').hidden = true;
}

/* ------------------------------------------------------------------
   Lesen — füllt die Seite: Zurück oben links, „Von NEU" und Datum,
   Betreff, Text, Anhänge als Pille; unten rechts Antworten und Löschen
   ------------------------------------------------------------------ */
function msgLesenSchliessen(){
  $('#msgRead').hidden = true;
  $('#msgReadActs').hidden = true;
  $('#msgList').hidden = false;
  $('#msgTabs').hidden = false;
  $('#msgBack').hidden = true;
  $('#msgSearch').hidden = false;
  $('#msgNew').hidden = false;
  document.body.classList.remove('is-msgread');
  /* Beim Lesen nimmt der Pfeil zurück den Platz des Kontoknopfes ein —
     zwei Knöpfe nebeneinander wären dort zu eng. */
  $('#btnSwitchEntity').hidden = false;
  msgOffen = null;
}

function msgAnhangPille(a){
  const pdf = /pdf/i.test(a.type || '') || /\.pdf$/i.test(a.name || '');
  return `<a class="msgfile" href="${MSG_URL}?action=attachment&id=${a.id}" download="${esc(a.name || 'Anhang')}">
       ${pdf ? MSG_PDF : MSG_DATEI}<span>${esc(a.name || 'Anhang')}</span></a>`;
}

async function msgOeffnen(id){
  msgPopsZu();
  let d;
  try { d = await msgFetch('detail', { id }); }
  catch (e) { toast('Mitteilung nicht gefunden.'); return; }
  const m = d.item;

  /* Ein Entwurf wird nicht gelesen, sondern weitergeschrieben. */
  if (m.status === 'draft'){ msgSchreiben(m); return; }

  msgOffen = m;
  const eingang = m.box ? m.box === 'posteingang' : msgBox === 'in';
  const wer = eingang ? `Von<b>${esc(m.sender || '')}</b>`
                      : `An<b>${esc(m.recipientsText || (m.recipients || []).map(r => r.label).join(', '))}</b>`;
  $('#msgRead').innerHTML = `
    <div class="msgread__head">
      <span class="msgread__from">${wer}</span>
      <span class="msgread__date">${msgDatum(m.sentAt, true)}</span>
    </div>
    <h2 class="msgread__subject">${esc(m.subject || 'Ohne Betreff')}</h2>
    <div class="msgread__body">${msgText(m.body)}</div>
    ${(m.attachments || []).length ? `<div class="msgread__files">${m.attachments.map(msgAnhangPille).join('')}</div>` : ''}`;

  $('#msgList').hidden = true;
  $('#msgTabs').hidden = true;
  $('#msgFind').hidden = true;
  $('#msgRead').hidden = false;
  $('#msgRead').scrollTop = 0;
  $('#msgReadActs').hidden = false;
  /* Antworten nur, wenn der Absender es erlaubt (und im Posteingang). */
  $('#msgReply').hidden = !(eingang && m.canReply !== false && m.replyAllowed !== false);
  $('#msgBack').hidden = false;
  $('#msgSearch').hidden = true;
  $('#msgNew').hidden = true;
  $('#btnSwitchEntity').hidden = true;
  document.body.classList.add('is-msgread');
  msgPunktHolen();
}

/* ------------------------------------------------------------------
   Schreiben — Editor wie in der iOS-App
   ------------------------------------------------------------------ */
function msgEmpfaengerZeigen(){
  const max = 3;
  const pillen = msgEmpfaenger.slice(0, max).map(e => `<span class="msgpill">${esc(e.label)}</span>`).join('');
  const mehr = msgEmpfaenger.length > max ? `<span class="msgpill msgpill--mehr">+${msgEmpfaenger.length - max}</span>` : '';
  $('#msgTo').innerHTML = pillen + mehr;
  $('#msgWriteTitle').textContent = 'Mitteilung an ' + (MSG_TYP_NAME[msgTyp] || 'Kolleg*innen');
}

function msgDateienZeigen(){
  $('#msgFiles').innerHTML = msgAnhaenge.map((f, i) =>
    `<span class="msgchip">${esc(f.name)}<button type="button" data-datei="${i}" aria-label="Entfernen">×</button></span>`).join('');
  $$('#msgFiles [data-datei]').forEach(b => b.addEventListener('click', () => {
    msgAnhaenge.splice(+b.dataset.datei, 1);
    msgDateienZeigen();
  }));
}

function msgOptZeigen(){
  $('#msgOptReceipt').setAttribute('aria-pressed', String(msgOpt.receipt));
  $('#msgOptNoReply').setAttribute('aria-pressed', String(msgOpt.noReply));
  $('#msgOptReceipt').classList.toggle('is-on', msgOpt.receipt);
  $('#msgOptNoReply').classList.toggle('is-on', msgOpt.noReply);
}

function msgSchreiben(entwurf, typ){
  msgEntwurf    = entwurf && entwurf.status === 'draft' ? entwurf.id : 0;
  msgTyp        = typ || (entwurf ? 'individuell' : 'kollegium');
  msgAntwortAuf = entwurf && entwurf.replyTo ? entwurf.replyTo : 0;
  msgEmpfaenger = entwurf ? (entwurf.recipients || []).filter(r => r.token).map(r =>
      ({ token:r.token, label:r.label, initials:initialen(r.label) })) : [];
  msgAnhaenge = [];
  msgOpt = { receipt: !!(entwurf && entwurf.readReceipt), noReply: !!(entwurf && entwurf.replyAllowed === false) };
  $('#msgSubject').value  = entwurf ? (entwurf.subject || '') : '';
  $('#msgBody').value     = entwurf ? (entwurf.body || '') : '';
  msgEmpfaengerZeigen();
  msgDateienZeigen();
  msgOptZeigen();
  SH.write.open();
}

/* Verfassen: erst wählen, an wen (Kolleg*innen / Individuell), dann
   öffnet sich der Editor mit der Empfängerauswahl darüber. */
function msgNeu(typ){
  msgPopsZu();
  toDaten = null;               /* frisch laden (Konto kann gewechselt haben) */
  msgSchreiben(null, typ);
  msgToOeffnen();
}

/* Antworten: Vorschlag des Servers (Empfänger, „AW: …", zitierter Text). */
async function msgAntworten(){
  const m = msgOffen;
  if (!m) return;
  let v = null;
  try { v = await msgFetch('reply', { id:m.id }); }
  catch (e) {
    if (/keine Antwort/i.test(e.message)) { toast(e.message); return; }
  }
  msgSchreiben(null, 'individuell');
  msgAntwortAuf = (v && v.replyTo) || m.id;
  msgEmpfaenger = v && v.recipients && v.recipients.length
    ? v.recipients.map(r => ({ token:r.token, label:r.label, initials:initialen(r.label) }))
    : (m.senderToken ? [{ token:m.senderToken, label:m.sender || m.senderToken, initials:initialen(m.sender) }] : []);
  $('#msgSubject').value = v && v.subject ? v.subject
    : (/^(re|aw):/i.test(m.subject || '') ? m.subject : 'AW: ' + (m.subject || ''));
  if (v && v.body) $('#msgBody').value = v.body;
  msgEmpfaengerZeigen();
}

async function msgAbschicken(senden){
  const daten = new FormData();
  daten.set('subject', $('#msgSubject').value.trim());
  daten.set('body',    $('#msgBody').value);
  daten.set('recipients', JSON.stringify(msgEmpfaenger.map(e => e.token)));
  daten.set('draft_id', String(msgEntwurf || 0));
  daten.set('read_receipt', msgOpt.receipt ? '1' : '0');
  daten.set('no_reply', msgOpt.noReply ? '1' : '0');
  if (msgAntwortAuf) daten.set('reply_to', String(msgAntwortAuf));
  msgAnhaenge.forEach(f => daten.append('attachments[]', f, f.name));
  const knopf = $('#msgSend');
  knopf.disabled = true;
  try {
    await msgFetch(senden ? 'send' : 'save_draft', {}, daten);
    SH.write.close();
    toast(senden ? 'Mitteilung gesendet.' : 'Entwurf gespeichert.');
    if (senden && msgBox === 'draft') msgBox = 'in';
    renderMessages();
  } catch (e) {
    const text = { recipient_required:'Bitte Empfänger wählen.',
                   subject_required:'Bitte einen Betreff angeben.' }[e.message];
    toast(text || (e.message && e.message !== 'Fehler' ? e.message : 'Nicht gesendet.'));
  } finally {
    knopf.disabled = false;
  }
}

async function msgLoeschen(){
  if (!msgOffen) return;
  try {
    await msgFetch('delete', {}, { id: String(msgOffen.id) });
    toast('Mitteilung gelöscht.');
    renderMessages();
  } catch (e) { toast('Nicht gelöscht.'); }
}

function msgDateienDazu(liste){
  const neu = [...liste];
  const zuGross = neu.filter(f => f.size > msgMax.mb * 1024 * 1024);
  if (zuGross.length) toast(`Ein Anhang darf höchstens ${msgMax.mb} MB groß sein.`);
  const passend = neu.filter(f => f.size <= msgMax.mb * 1024 * 1024);
  if (msgAnhaenge.length + passend.length > msgMax.files) toast(`Höchstens ${msgMax.files} Anhänge.`);
  msgAnhaenge = msgAnhaenge.concat(passend.slice(0, Math.max(0, msgMax.files - msgAnhaenge.length)));
  msgDateienZeigen();
}

/* ------------------------------------------------------------------
   Empfänger wählen — Blatt wie in der iOS-App: Filter-Pillen
   (Ausgewählt, Liste, Klasse, Abteilung), „Alle auswählen", Zeilen mit
   Auswahlkreis, Avatar, Kürzel und Rollen; Suchfeld schwebt unten.
   ------------------------------------------------------------------ */
/* TO-BEGINN – Filter der Empfängerauswahl (in sich geschlossen; die Tests
   führen diesen Abschnitt mit Node aus). */
function toFiltern(personen, f, gewaehlt){
  const q = String(f.q || '').trim().toLowerCase();
  const liste = f.liste ? new Set(f.liste.members || []) : null;
  return personen.filter(p => {
    if (f.ausgewaehlt && !gewaehlt.has(p.token)) return false;
    if (liste && !liste.has(p.token)) return false;
    if (f.klasse && !(p.classes || []).includes(f.klasse)) return false;
    if (f.abteilung && !(p.departments || []).includes(f.abteilung)) return false;
    if (q){
      const text = [p.label, p.fullName, p.short, ...(p.roles || []).map(r => r.text), ...(p.classes || [])]
        .filter(Boolean).join(' ').toLowerCase();
      if (!text.includes(q)) return false;
    }
    return true;
  });
}
if (typeof window !== 'undefined') window.UntPlanEmpfaenger = { filtern: toFiltern };
/* TO-ENDE */

let toDaten = null;           /* Antwort von action=people */
let toTypGeladen = '';
let toWahl = new Map();       /* token → {token, label, initials} */
let toFilter = { ausgewaehlt:false, liste:null, klasse:'', abteilung:'', q:'' };
let toErstes = false;         /* erstes Öffnen direkt nach „Verfassen" */

function toAlle(){
  if (!toDaten) return [];
  return [...(toDaten.items || []), ...(toDaten.groups || [])];
}

async function msgToOeffnen(){
  toErstes = !msgEmpfaenger.length;
  toWahl = new Map(msgEmpfaenger.map(e => [e.token, e]));
  toFilter = { ausgewaehlt:false, liste:null, klasse:'', abteilung:'', q:'' };
  $('#toQuery').value = '';
  $('#toSub').textContent = MSG_TYP_NAME[msgTyp] || 'Kolleg*innen';
  $('#toPop').hidden = true;
  SH.to.open();
  if (!toDaten || toTypGeladen !== msgTyp){
    toDaten = null;
    $('#toList').innerHTML = '<p class="to__hint">Wird geladen …</p>';
    $('#toFilter').innerHTML = '';
    try {
      toDaten = await msgFetch('people', { type: msgTyp });
      toTypGeladen = msgTyp;
    } catch (e) {
      $('#toList').innerHTML = '<p class="to__hint">Die Empfänger ließen sich nicht laden.</p>';
      return;
    }
  }
  toZeigen();
}

const TO_CHEV = '<svg viewBox="0 0 24 24" class="ico" aria-hidden="true"><path d="m7 10 5 5 5-5"/></svg>';
const TO_HAKEN = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m6.5 12.5 3.8 3.8L17.5 9"/></svg>';

function toZeigen(){
  if (!toDaten) return;
  const f = toFilter;
  const pill = (key, text, an, menue) =>
    `<button type="button" class="to__pill ${an ? 'is-on' : ''}" data-f="${key}" aria-pressed="${an}">${esc(text)}${menue ? TO_CHEV : ''}</button>`;
  let html = pill('ausgewaehlt', 'Ausgewählt', f.ausgewaehlt, false);
  if ((toDaten.lists || []).length) html += pill('liste', f.liste ? f.liste.name : 'Liste', !!f.liste, true);
  if ((toDaten.classes || []).length) html += pill('klasse', f.klasse || 'Klasse', !!f.klasse, true);
  if ((toDaten.departments || []).length) html += pill('abteilung', f.abteilung || 'Abteilung', !!f.abteilung, true);
  $('#toFilter').innerHTML = html;

  const sichtbar = toFiltern(toAlle(), f, new Set(toWahl.keys()));
  const alleAn = sichtbar.length > 0 && sichtbar.every(p => toWahl.has(p.token));
  const radio = an => `<span class="to__radio ${an ? 'is-on' : ''}">${an ? TO_HAKEN : ''}</span>`;
  const zeile = (p, i) => {
    const an = toWahl.has(p.token);
    const rollen = (p.roles || []).filter(r => r.text).map(r =>
      `<span class="to__rolle ${String(r.text).length > 34 ? 'is-lang' : ''}">${esc(r.text)}</span>`).join('');
    return `<button type="button" class="to__row" data-i="${i}" role="checkbox" aria-checked="${an}">
        ${radio(an)}
        <span class="to__ava">${esc(p.initials || initialen(p.label))}</span>
        <span class="to__main"><span class="to__name">${esc(p.label)}</span>${rollen ? `<span class="to__rollen">${rollen}</span>` : ''}</span>
      </button>`;
  };
  $('#toList').innerHTML = sichtbar.length
    ? `<button type="button" class="to__row to__row--alle" data-alle role="checkbox" aria-checked="${alleAn}">${radio(alleAn)}<span class="to__alle">Alle auswählen</span></button>`
      + sichtbar.map(zeile).join('')
    : '<p class="to__hint">Keine Treffer.</p>';
  $('#toList')._sichtbar = sichtbar;
  $('#toOk').disabled = toWahl.size === 0;
}

function toMenue(key, knopf){
  const pop = $('#toPop');
  if (!pop.hidden && pop.dataset.f === key){ pop.hidden = true; return; }
  let werte = [];
  if (key === 'liste') werte = (toDaten.lists || []).map(l => ({ v:l.token, t:l.name, an: toFilter.liste && toFilter.liste.token === l.token }));
  if (key === 'klasse') werte = (toDaten.classes || []).map(k => ({ v:k, t:k, an: toFilter.klasse === k }));
  if (key === 'abteilung') werte = (toDaten.departments || []).map(a => ({ v:a, t:a, an: toFilter.abteilung === a }));
  pop.dataset.f = key;
  pop.innerHTML = `<button type="button" data-v="" class="${werte.some(w => w.an) ? '' : 'is-on'}">Alle</button>`
    + werte.map(w => `<button type="button" data-v="${esc(w.v)}" class="${w.an ? 'is-on' : ''}">${esc(w.t)}</button>`).join('');
  const rb = knopf.getBoundingClientRect(), rs = $('#sheetTo').getBoundingClientRect();
  pop.style.top = (rb.bottom - rs.top + 6) + 'px';
  pop.style.left = Math.max(10, Math.min(rb.left - rs.left, rs.width - 230)) + 'px';
  pop.hidden = false;
}

$('#toFilter').addEventListener('click', e => {
  const b = e.target.closest('[data-f]');
  if (!b || !toDaten) return;
  const key = b.dataset.f;
  if (key === 'ausgewaehlt'){ toFilter.ausgewaehlt = !toFilter.ausgewaehlt; $('#toPop').hidden = true; toZeigen(); return; }
  toMenue(key, b);
});
$('#toPop').addEventListener('click', e => {
  const b = e.target.closest('[data-v]');
  if (!b) return;
  const key = $('#toPop').dataset.f, v = b.dataset.v;
  if (key === 'liste') toFilter.liste = v ? (toDaten.lists || []).find(l => l.token === v) || null : null;
  if (key === 'klasse') toFilter.klasse = v;
  if (key === 'abteilung') toFilter.abteilung = v;
  $('#toPop').hidden = true;
  toZeigen();
});
$('#toList').addEventListener('click', e => {
  const b = e.target.closest('.to__row');
  if (!b) return;
  $('#toPop').hidden = true;
  const sichtbar = $('#toList')._sichtbar || [];
  if (b.hasAttribute('data-alle')){
    const alleAn = sichtbar.every(p => toWahl.has(p.token));
    sichtbar.forEach(p => { if (alleAn) toWahl.delete(p.token); else toWahl.set(p.token, { token:p.token, label:p.label, initials:p.initials }); });
  } else {
    const p = sichtbar[+b.dataset.i];
    if (!p) return;
    if (toWahl.has(p.token)) toWahl.delete(p.token);
    else toWahl.set(p.token, { token:p.token, label:p.label, initials:p.initials });
  }
  const top = $('#toList').scrollTop;
  toZeigen();
  $('#toList').scrollTop = top;
});
$('#toQuery').addEventListener('input', () => {
  clearTimeout(msgSucheTimer);
  msgSucheTimer = setTimeout(() => { toFilter.q = $('#toQuery').value; toZeigen(); }, 120);
});
$('#toOk').addEventListener('click', () => {
  msgEmpfaenger = [...toWahl.values()];
  msgEmpfaengerZeigen();
  toErstes = false;
  SH.to.close();
});
$('#toClose').addEventListener('click', () => {
  SH.to.close();
  /* Direkt nach „Verfassen" ohne Auswahl abgebrochen: auch der Editor geht zu. */
  if (toErstes && !msgEmpfaenger.length && !$('#msgSubject').value.trim() && !$('#msgBody').value.trim()) SH.write.close();
  toErstes = false;
});
$('#sheetTo').addEventListener('click', e => {
  const pop = $('#toPop');
  if (!pop.hidden && !e.target.closest('#toPop') && !e.target.closest('[data-f]')) pop.hidden = true;
});

/* ------------------------------------------------------------------
   Bedienung
   ------------------------------------------------------------------ */
$('#msgBoxBtn').addEventListener('click', e => {
  e.stopPropagation();
  $('#msgNewPop').hidden = true;
  const pop = $('#msgBoxPop');
  pop.hidden = !pop.hidden;
});
$$('#msgBoxPop [data-box]').forEach(b => b.addEventListener('click', () => {
  msgBox = b.dataset.box;
  renderMessages();
}));

$('#msgBack').addEventListener('click', msgLesenSchliessen);
$('#msgReply').addEventListener('click', msgAntworten);
$('#msgDelete').addEventListener('click', msgLoeschen);
$('#msgSearch').addEventListener('click', () => {
  msgPopsZu();
  const f = $('#msgFind');
  f.hidden = !f.hidden;
  if (!f.hidden) $('#msgQuery').focus();
  else if (msgSuche){ msgSuche = ''; $('#msgQuery').value = ''; renderMessages(); }
});
$('#msgQuery').addEventListener('input', () => {
  clearTimeout(msgSucheTimer);
  msgSucheTimer = setTimeout(() => { msgSuche = $('#msgQuery').value.trim(); renderMessages(); }, 260);
});
$('#msgNew').addEventListener('click', e => {
  e.stopPropagation();
  $('#msgBoxPop').hidden = true;
  /* Schüler*innen und Erziehungsberechtigte schreiben nur den erlaubten
     Lehrkräften — für sie gibt es keine Wahl „Kolleg*innen". */
  if (state.isStudent){ msgNeu('individuell'); return; }
  const pop = $('#msgNewPop');
  pop.hidden = !pop.hidden;
});
$$('#msgNewPop [data-typ]').forEach(b => b.addEventListener('click', () => msgNeu(b.dataset.typ)));
document.addEventListener('click', e => {
  if (!e.target.closest('#msgNewPop, #msgNew, #msgBoxPop, #msgBoxBtn')) msgPopsZu();
});
$('#msgClose').addEventListener('click', () => SH.write.close());
$('#msgToRow').addEventListener('click', msgToOeffnen);
$('#msgSend').addEventListener('click', () => msgAbschicken(true));
$('#msgOptReceipt').addEventListener('click', () => {
  msgOpt.receipt = !msgOpt.receipt; msgOptZeigen();
  toast(msgOpt.receipt ? 'Lesebestätigung wird angefordert.' : 'Ohne Lesebestätigung.');
});
$('#msgOptNoReply').addEventListener('click', () => {
  msgOpt.noReply = !msgOpt.noReply; msgOptZeigen();
  toast(msgOpt.noReply ? 'Antwort nicht erlaubt.' : 'Antwort erlaubt.');
});
['#msgAttach', '#msgPhoto', '#msgCamera'].forEach(sel => $(sel).addEventListener('change', e => {
  msgDateienDazu(e.target.files);
  e.target.value = '';
}));
/* Grenzen für Anhänge vom Server (still, mit Vorgaben als Rückhalt). */
msgFetch('options').then(o => { msgMax = { files: o.maxAttachments || 5, mb: o.maxAttachmentMb || 7 }; }).catch(() => {});

$('#btnAbout').addEventListener('click', () =>
  toast('UntPlanMobile · Fassung ' + BUILD));

/* Erscheinungsbild reihum: Automatisch → Hell → Dunkel */

$('#btnSwitchEntity2').addEventListener('click', openPicker);
/* =====================================================================
   Startseite: Termine, Abwesenheiten, Klassenleitung
   ---------------------------------------------------------------------
   Drei Listen aus derselben Schnittstelle. Der Aufbau folgt der Vorlage:
   Kopfzeile mit Titel und Zeitraum, darunter Karten mit Datum, Uhrzeit,
   Anlass und Stand.
   ===================================================================== */
const HOME_URL = new URL('api/home', location.href).href;
let listeStand = { art:null, von:null, bis:null, klasse:null, zeitraum:null, stand:'alle', umfang:'mine', kind:null };

async function homeFetch(action, extra){
  const p = new URLSearchParams(Object.assign({ action }, extra || {}));
  if (listeStand.von) p.set('from', listeStand.von);
  if (listeStand.bis) p.set('to',   listeStand.bis);
  const r = await fetch(HOME_URL + '?' + p, { credentials:'same-origin' });
  const d = await r.json().catch(() => null);
  if (!d || !d.ok) throw new Error((d && d.error) || 'Fehler');
  return d;
}

const deTag = iso => {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(String(iso || ''));
  if (!m) return String(iso || '');
  const d = new Date(+m[1], +m[2] - 1, +m[3]);
  return `${d.getDate()}. ${MONTHS_SHORT[d.getMonth()]}`;
};

const STAND_ICO = {
  ok:   '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/><path d="m8.2 12.3 2.6 2.6L16 9.8"/></svg>',
  open: '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/></svg>',
  no:   '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/></svg>'
};

function listeOeffnen(art, extra){
  /* Die Klassenleitung startet mit den eigenen Klassen und den offenen
     Fällen — das ist, was man dort zuerst braucht. */
  if (art === 'leadabsent' && listeStand.art !== 'leadabsent'){
    listeStand.umfang = 'mine'; listeStand.stand = 'offen';
    $('#lstFilterBar').hidden = false;
  }
  listeStand.art = art;
  listeStand.klasse = (extra && extra.class) || listeStand.klasse;
  showScreen('list');
  $('#lstBody').innerHTML = '<div class="lst__empty"><span>Wird geladen …</span></div>';
  listeZeichnen();
}

/* Leerbild wie in der Vorlage: Zeichen im hellen Kreis, dann die beiden
   Zeilen darunter. */
const LEER = (titel, text) => `
  <div class="lst__empty">
    <span class="lst__emptyIco">
      <svg viewBox="0 0 24 24">
        <ellipse cx="12" cy="6.4" rx="7" ry="2.8"/>
        <path d="M5 6.4v5.2c0 1.5 3.1 2.8 7 2.8 1 0 2-.1 2.8-.3"/>
        <path d="M5 11.6v5.2c0 1.4 2.6 2.6 6.1 2.8"/>
        <circle cx="17.4" cy="17.4" r="3.9"/>
        <path d="M16.3 16.3a1.2 1.2 0 0 1 2.2.7c0 .8-1.1 1-1.1 1.7"/>
        <circle cx="17.4" cy="20.1" r=".5" fill="currentColor" stroke="none"/>
      </svg>
    </span>
    <b>${titel}</b>
    <span>${text}</span>
  </div>`;

async function listeZeichnen(){
  const art = listeStand.art;
  /* Sprechtag: eigene Ansicht ohne Filter und Suche. */
  $('#screenList').classList.toggle('is-spt', art === 'sprechtag');
  if (art === 'sprechtag') return sprechtagZeichnen();
  const titel = { exams:'Meine Termine', absences:'Meine Abwesenheiten',
                  leadabsent:'Abwesenheiten', classlead:'Klassenleitung' }[art] || '';
  $('#lstTitle').textContent = titel;

  let d;
  try {
    /* Klassenleitung: Abwesenheiten mit Stand der Fachlehrer-Entschuldigungen
       (UntPlanWeb, Entschuldigungen). Ältere Server kennen den Endpunkt
       nicht — dann bleibt es bei der bisherigen Liste. */
    const ent = art === 'leadabsent' ? await entLaden().catch(() => null) : null;
    d = ent ? ent : await homeFetch(art, art === 'leadabsent' || art === 'classlead'
                              ? { class: listeStand.klasse || '',
                                  scope: listeStand.umfang || 'mine' }
                              : art === 'absences' && listeStand.kind ? { student: String(listeStand.kind) } : null);
  } catch (e){
    $('#lstBody').innerHTML = `<div class="lst__empty"><b>Nicht erreichbar</b><span>${esc(e.message)}</span></div>`;
    return;
  }
  /* In der Vorlage steht dort nur das Ende des Zeitraums. */
  $('#lstRange').textContent = d.from ? `${deDate(d.from)} - ${deDate(d.to)}` : `bis ${deDate(d.to)}`;
  /* Die eigenen Klassen für den Filter merken. */
  if (d.classes) listeStand.klassen = d.classes;

  /* Suche über alles, was auf der Karte steht. */
  if (lstSuche){
    const passt = x => JSON.stringify(x).toLowerCase().includes(lstSuche);
    if (d.items) d.items = d.items.filter(passt);
    if (d.groups) Object.keys(d.groups).forEach(k => d.groups[k] = d.groups[k].filter(passt));
  }

  if (art === 'exams'){
    $('#lstBody').innerHTML = d.items.length ? d.items.map(x => `
      <article class="lstcard">
        <b>${esc(deTag(x.date))}</b>
        <p class="lstcard__line"><svg viewBox="0 0 24 24" class="lstcard__ico"><circle cx="12" cy="12" r="8.6"/><path d="M12 7.2V12l3.1 2"/></svg>${esc(x.timeFrom)} – ${esc(x.timeTo)}</p>
        <p class="lstcard__line"><svg viewBox="0 0 24 24" class="lstcard__ico"><circle cx="12" cy="12" r="8.6"/><path d="M12 11.2v5.4"/><circle cx="12" cy="7.9" r=".95" fill="currentColor" stroke="none"/></svg>${esc([x.art, x.subject, x.class].filter(Boolean).join(' · '))}</p>
        ${x.room ? `<p class="lstcard__sub">Raum ${esc(x.room)}</p>` : ''}
        ${x.text ? `<p class="lstcard__sub">${esc(x.text)}</p>` : ''}
      </article>`).join('')
      : LEER('Keine Daten', 'Derzeit gibt es hier noch nichts');
    return;
  }

  if (art === 'absences'){
    /* Stand-Filter: alles, nur offene oder nur entschuldigte. */
    const st = listeStand.stand || 'alle';
    d.items = (d.items || []).filter(x => st === 'alle'
      || (st === 'offen' && x.tone !== 'ok')
      || (st === 'ok'    && x.tone === 'ok'));
    /* map() reicht den Index als zweites Argument weiter — ohne die
       Klammer hier hielte die Karte ihn für „mit Namen". */
    /* Schüler*innen/Eltern: Kind wählen (mehrere Kinder). Oben steht
       bewusst keine Box mit Summen bzw. Statistik — nur die Liste. */
    const kopf = d.student ? absKinder(d) : '';
    $('#lstBody').innerHTML = kopf + (d.items.length ? d.items.map(x => karteAbwesenheit(x)).join('')
      : (d.student
          ? LEER('Keine Daten', 'Derzeit gibt es hier noch nichts')
          : `<div class="lst__empty"><b>Kein Schülerkonto zugeordnet</b>
               <span>Dieses Konto ist mit keinem Schüler verknüpft — deshalb
               lassen sich keine Abwesenheiten anzeigen.</span></div>`));
    $$('#lstBody [data-kind]').forEach(b => b.addEventListener('click', () => {
      listeStand.kind = +b.dataset.kind;
      listeZeichnen();
    }));
    return;
  }

  if (art === 'leadabsent' && d.entschuldigungen){
    entListeZeichnen(d.entschuldigungen);
    return;
  }
  if (art === 'leadabsent'){
    const stL = listeStand.stand || 'alle';
    const G = [['pending','Offen'], ['approved','Entschuldigt'], ['rejected','Nicht anerkannt']]
      .filter(([k]) => stL === 'alle' || (stL === 'offen' && k === 'pending')
                                     || (stL === 'ok' && k !== 'pending'));
    let html = '';
    G.forEach(([k, n]) => {
      const items = (d.groups && d.groups[k]) || [];
      if (!items.length) return;
      html += `<h3 class="lst__group">${n} (${items.length})</h3>`
            + items.map(x => karteAbwesenheit(x, true)).join('');
    });
    $('#lstBody').innerHTML = html || LEER('Keine Daten', 'Derzeit gibt es hier noch nichts');
    $$('#lstBody [data-excuse]').forEach(b => b.addEventListener('click', async () => {
      try {
        await fetch(HOME_URL + '?action=excuse', {
          method:'POST', credentials:'same-origin',
          headers:{ 'Content-Type':'application/x-www-form-urlencoded' },
          body: new URLSearchParams({ action:'excuse', id:b.dataset.excuse, status:b.dataset.stand })
        }).then(r => r.json()).then(r => { if (!r.ok) throw new Error(r.error || 'Fehler'); });
        buzz(); listeZeichnen();
      } catch (e){ toast('Nicht gespeichert: ' + e.message); }
    }));
  }
}

/* =====================================================================
   Entschuldigungen der Klassenleitung
   ---------------------------------------------------------------------
   Abwesenheiten (Zeiträume) der eigenen Klassen mit dem Stand der
   Fachlehrer-Entschuldigungen. Wischen nach rechts entschuldigt, nach
   links stehen „Nicht entschuldigt“ und „Zurücknehmen“; Tippen öffnet
   den Dialog „Entschuldigung erfassen“ (wie „Abwesenheit bearbeiten“)
   mit Status, Datum, Nachweis und Text. Rechte prüft UntPlanWeb.
   ===================================================================== */
const ENT_URL = 'api/entschuldigungen/';
const ENT_STATUS = { offen:'offen', entschuldigt:'entschuldigt', unentschuldigt:'nicht entschuldigt' };
const ENT_TON = { offen:'open', entschuldigt:'ok', unentschuldigt:'no' };
const ENT_NACHWEIS = [['', 'Kein Nachweis'], ['attest', 'Attest'], ['schriftlich', 'Schriftlich'], ['muendlich', 'Mündlich'],
                      ['telefonisch', 'Telefonisch'], ['email', 'E-Mail']];
let entListe = [];
let entOffen = null;     /* Abwesenheit im Dialog */
let entEntwurf = null;   /* { status, datum, nachweis, text } */

async function entFetch(pfad, body){
  const r = await fetch(ENT_URL + pfad, body ? {
    method:'POST', credentials:'same-origin',
    headers:{ 'Content-Type':'application/json' }, body: JSON.stringify(body)
  } : { credentials:'same-origin' });
  const d = await r.json().catch(() => null);
  if (!r.ok) throw new Error((d && (d.fehler || d.error)) || ('Fehler ' + r.status));
  return d;
}

async function entLaden(){
  const e = await entFetch('einstellungen');
  if (!e || !e.klassenleitung) return null;
  const heute = new Date();
  const sj = new Date(heute.getMonth() >= 7 ? heute.getFullYear() : heute.getFullYear() - 1, 7, 1);
  const p = new URLSearchParams({ von: listeStand.von || iso(sj), bis: listeStand.bis || iso(heute) });
  if (listeStand.umfang !== 'all' && listeStand.klasse) p.set('klasse', listeStand.klasse);
  if ((listeStand.stand || 'alle') === 'offen') p.set('status', 'offen');
  const liste = await entFetch('abwesenheiten?' + p);
  if (!Array.isArray(liste)) return null;
  return { from: p.get('von'), to: p.get('bis'), entschuldigungen: liste,
           classes: (e.klassen || []).map(k => ({ id:k, name:k })) };
}

/* „Mo., 12.10.  08:00 – 13:15“ bzw. über mehrere Tage mit Wochentag. */
function entZeitraum(a){
  const tag = d => { const x = parseISO(d); return `${WD_SHORT[(x.getDay() + 6) % 7]}, ${deDate(d)}`; };
  return (!a.bisDatum || a.bisDatum === a.vonDatum)
    ? `${tag(a.vonDatum)}  ${a.vonZeit || ''} – ${a.bisZeit || ''}`
    : `${tag(a.vonDatum)} ${a.vonZeit || ''} – ${tag(a.bisDatum)} ${a.bisZeit || ''}`;
}

function entKarte(a, i){
  const ton = a.mehrdeutig ? 'open' : (ENT_TON[a.status] || 'open');
  const stand = a.mehrdeutig ? 'offen – Lehrkräfte uneinig' : (ENT_STATUS[a.status] || a.status || 'offen');
  const text = String(a.text || '').trim();
  const darf = a.darf !== false;
  const zurueck = darf && (a.status !== 'offen' || (a.stunden || []).some(s => s.status !== 'offen'));
  return `<div class="ent-row" data-i="${i}">
      ${darf ? `<div class="ent-row__acts ent-row__acts--l">
        <button type="button" class="ent-row__act is-gruen" data-ent="entschuldigt">
          <svg viewBox="0 0 24 24" class="ico"><path d="m5 12.5 4.5 4.5L19 7"/></svg>Entschuldigt</button>
      </div>
      <div class="ent-row__acts ent-row__acts--r">
        <button type="button" class="ent-row__act is-rot" data-ent="unentschuldigt">
          <svg viewBox="0 0 24 24" class="ico"><path d="m6 6 12 12M18 6 6 18"/></svg>Nicht entsch.</button>
        ${zurueck ? `<button type="button" class="ent-row__act is-grau" data-ent="offen">
          <svg viewBox="0 0 24 24" class="ico"><path d="M9 7 4.5 11.5 9 16"/><path d="M5 11.5h9.5a5 5 0 0 1 0 10H12"/></svg>Zurück&shy;nehmen</button>` : ''}
      </div>` : ''}
      <article class="lstcard ent-row__front" role="button" tabindex="0">
        <b>${esc(a.schueler || '')}</b>
        <p class="lstcard__sub">${esc(a.klasse || '')}${a.fehlstunden ? ` · ${a.fehlstunden} ${a.fehlstunden === 1 ? 'Stunde' : 'Stunden'}` : ''}</p>
        <p class="lstcard__line"><svg viewBox="0 0 24 24" class="lstcard__ico"><circle cx="12" cy="12" r="8.6"/><path d="M12 7.2V12l3.1 2"/></svg>${esc(entZeitraum(a))}</p>
        ${text ? `<p class="lstcard__line lstcard__line--note"><svg viewBox="0 0 24 24" class="lstcard__ico"><path d="M15.6 3.4H6.3A1.8 1.8 0 0 0 4.5 5.2v13.6a1.8 1.8 0 0 0 1.8 1.8h11.4a1.8 1.8 0 0 0 1.8-1.8V6.9z"/><path d="M7.8 8.8h7.8M7.8 12h7.8M7.8 15.2h4"/></svg><span>${esc(text)}</span></p>` : ''}
        <p class="lstcard__line lstcard__stand lstcard__stand--${ton}">
          <svg viewBox="0 0 24 24" class="lstcard__ico">${STAND_ICO[ton] || ''}</svg>${esc(stand)}${
          a.lehrerGesamt ? ` <span class="lstcard__sm">· ${a.lehrerBearbeitet || 0}/${a.lehrerGesamt} Lehrkräfte bearbeitet</span>` : ''}</p>
      </article>
    </div>`;
}

function entListeZeichnen(liste){
  const st = listeStand.stand || 'alle';
  entListe = (liste || []).filter(a => st === 'alle' || (st === 'offen' && (a.status === 'offen' || a.mehrdeutig))
                                                     || (st === 'ok' && a.status !== 'offen' && !a.mehrdeutig));
  if (lstSuche) entListe = entListe.filter(a => JSON.stringify(a).toLowerCase().includes(lstSuche));
  const gruppen = [['offen', 'Offen'], ['entschuldigt', 'Entschuldigt'], ['unentschuldigt', 'Nicht entschuldigt']];
  let html = entListe.length ? '<p class="ent__tipp">Nach rechts wischen: entschuldigt · nach links: nicht entschuldigt oder zurücknehmen · Tippen: bearbeiten</p>' : '';
  gruppen.forEach(([k, n]) => {
    const teil = entListe.map((a, i) => [a, i]).filter(([a]) => (a.mehrdeutig ? 'offen' : a.status) === k);
    if (!teil.length) return;
    html += `<h3 class="lst__group">${n} (${teil.length})</h3>` + teil.map(([a, i]) => entKarte(a, i)).join('');
  });
  $('#lstBody').innerHTML = html || LEER('Keine Abwesenheiten', 'Im gewählten Zeitraum ist niemand abwesend');
  $$('#lstBody .ent-row').forEach(z => {
    const a = entListe[+z.dataset.i];
    entWischen(z, () => entOeffnen(a), status => entSetzen(a, status));
    z.querySelectorAll('[data-ent]').forEach(b => b.addEventListener('click', e => {
      e.stopPropagation();
      entSetzen(a, b.dataset.ent);
    }));
  });
}

/* Wischen nach beiden Seiten: rechts (Inhalt nach rechts) liegt
   „Entschuldigt“, links „Nicht entschuldigt“ und „Zurücknehmen“. Ganz
   durchgewischt löst die erste Aktion der Seite sofort aus. */
function entWischen(zeile, beimTippen, ausloesen){
  const vorn = zeile.querySelector('.ent-row__front');
  const seite = k => (zeile.querySelector('.ent-row__acts--' + k) || {}).offsetWidth || 0;
  let x0 = 0, y0 = 0, dx = 0, start = 0, aktiv = false, quer = null, id = null;
  const setze = (x, weich) => {
    vorn.style.transition = weich ? '' : 'none';
    vorn.style.transform = x ? `translateX(${x}px)` : '';
    zeile.classList.toggle('zeigt-l', x > 0);
    zeile.classList.toggle('zeigt-r', x < 0);
  };
  const lage = () => zeile.classList.contains('is-auf-l') ? seite('l') : zeile.classList.contains('is-auf-r') ? -seite('r') : 0;
  vorn.addEventListener('pointerdown', e => {
    if (!seite('l') && !seite('r')) return;
    $$('#lstBody .ent-row.is-auf-l, #lstBody .ent-row.is-auf-r').forEach(r => {
      if (r === zeile) return;
      r.classList.remove('is-auf-l', 'is-auf-r', 'zeigt-l', 'zeigt-r');
      r.querySelector('.ent-row__front').style.transform = '';
    });
    x0 = e.clientX; y0 = e.clientY; dx = 0; quer = null; aktiv = true; id = e.pointerId; start = lage();
  });
  vorn.addEventListener('pointermove', e => {
    if (!aktiv || e.pointerId !== id) return;
    const mx = e.clientX - x0, my = e.clientY - y0;
    if (quer === null && (Math.abs(mx) > 8 || Math.abs(my) > 8)){
      quer = Math.abs(mx) > Math.abs(my);
      if (quer) try { vorn.setPointerCapture(id); } catch (err){}
    }
    if (!quer) return;
    dx = mx;
    const w = zeile.offsetWidth;
    setze(Math.max(seite('r') ? -w : 0, Math.min(seite('l') ? w : 0, start + dx)), false);
  });
  const ende = () => {
    if (!aktiv) return;
    aktiv = false;
    if (!quer) return;
    const x = start + dx, w = zeile.offsetWidth;
    zeile.dataset.gewischt = '1';
    setTimeout(() => { delete zeile.dataset.gewischt; }, 350);
    zeile.classList.remove('is-auf-l', 'is-auf-r');
    if (x > w * 0.55 && seite('l')){ setze(0, true); buzz(); ausloesen('entschuldigt'); return; }
    if (x < -w * 0.55 && seite('r')){ setze(0, true); buzz(); ausloesen('unentschuldigt'); return; }
    if (x > seite('l') / 2 && seite('l')){ zeile.classList.add('is-auf-l'); setze(seite('l'), true); return; }
    if (x < -seite('r') / 2 && seite('r')){ zeile.classList.add('is-auf-r'); setze(-seite('r'), true); return; }
    setze(0, true);
  };
  vorn.addEventListener('pointerup', ende);
  vorn.addEventListener('pointercancel', ende);
  vorn.addEventListener('click', () => {
    if (zeile.dataset.gewischt) return;
    if (lage()){ zeile.classList.remove('is-auf-l', 'is-auf-r'); setze(0, true); return; }
    beimTippen();
  });
}

/* Status der ganzen Abwesenheit setzen (überschreibt die Fachlehrkräfte;
   abweichende Stunden werden vorher genannt). „offen“ nimmt zurück. */
async function entSetzen(a, status, extra){
  if (!a) return false;
  try {
    if (status === 'offen'){
      await entFetch('zuruecknehmen', { ids: a.ids });
      toast('Entschuldigung zurückgenommen');
    } else {
      const anders = (a.stunden || []).filter(s => s.entschuldigung && s.status !== 'offen' && s.status !== status);
      if (anders.length && !confirm(`${anders.length === 1 ? 'Eine Stunde ist' : anders.length + ' Stunden sind'} von Fachlehrkräften anders eingetragen (${
            anders.map(s => `${s.stunde}. ${s.fach || ''} ${s.lehrer || ''}: ${ENT_STATUS[s.status] || s.status}`.trim()).join('; ')}). Trotzdem für die ganze Abwesenheit übernehmen?`))
        return false;
      await entFetch('abwesenheit', Object.assign({ ids: a.ids, status, datum: iso(new Date()) }, extra || {}));
      toast(status === 'entschuldigt' ? 'Entschuldigt' : 'Als nicht entschuldigt eingetragen');
    }
    buzz();
    listeZeichnen();
    return true;
  } catch (e){
    toast('Nicht gespeichert: ' + e.message);
    listeZeichnen();
    return false;
  }
}

function entOeffnen(a){
  entOffen = a;
  entEntwurf = { status: a.mehrdeutig ? 'offen' : (a.status || 'offen'), datum: a.entschuldigtAm || iso(new Date()),
                 nachweis: a.nachweis || '', text: '' };
  entZeichnen();
  SH.ent.open();
}

function entSammeln(){
  if (!entEntwurf) return;
  const d = $('#entDatum'), n = $('#entNachweis'), t = $('#entText');
  if (d) entEntwurf.datum = d.value;
  if (n) entEntwurf.nachweis = n.value;
  if (t) entEntwurf.text = t.value;
}

function entZeichnen(){
  const a = entOffen, d = entEntwurf;
  if (!a || !d) return;
  const darf = a.darf !== false;
  const ton = s => ENT_TON[s] || 'open';
  const stunden = (a.stunden || []).map(s => `
      <li class="ent__stunde">
        <span class="ent__punkt ent__punkt--${ton(s.status)}"></span>
        <span class="ent__std"><b>${esc(deDate(s.datum))} · ${s.stunde}. Std.</b>
          <span>${esc([s.fach, s.lehrer].filter(Boolean).join(' · '))} – ${esc(ENT_STATUS[s.status] || s.status)}</span>
          ${s.entschuldigung && s.entschuldigung.text ? `<em>„${esc(s.entschuldigung.text)}“ (${esc(s.entschuldigung.lehrer || '')})</em>` : ''}
        </span>
      </li>`).join('');
  $('#entTitel').textContent = 'Entschuldigung';
  $('#entBody').innerHTML = `
    <div class="dabs__who">
      <span class="dabs__ava">${esc(absInitials(a.schueler || ''))}</span>
      <span class="dabs__id"><b>${esc(a.schueler || '')}</b>${a.klasse ? `<span class="dabs__chip">${esc(a.klasse)}</span>` : ''}</span>
    </div>
    <p class="ent__zeit">${esc(entZeitraum(a))}${a.fehlstunden ? ` · ${a.fehlstunden} ${a.fehlstunden === 1 ? 'Stunde' : 'Stunden'}` : ''}</p>
    ${String(a.text || '').trim() ? `<p class="ent__text">${esc(a.text)}</p>` : ''}

    <span class="dabs__lab">STATUS</span>
    <div class="ent__seg" role="radiogroup">
      ${['offen', 'entschuldigt', 'unentschuldigt'].map(k => `<button type="button" role="radio" data-st="${k}"
        class="ent__segbtn ent__segbtn--${ton(k)} ${d.status === k ? 'is-on' : ''}" aria-checked="${d.status === k}">${
        k === 'offen' ? 'Offen' : k === 'entschuldigt' ? 'Entschuldigt' : 'Nicht entsch.'}</button>`).join('')}
    </div>

    <div class="dabs__grid">
      <div>
        <span class="dabs__lab">EINGANG</span>
        <div class="dabs__fields"><input class="dabs__pill" type="date" id="entDatum" value="${esc(d.datum)}"></div>
      </div>
      <div>
        <span class="dabs__lab">NACHWEIS</span>
        <div class="dabs__fields"><select class="dabs__pill" id="entNachweis">${ENT_NACHWEIS.map(([v, n]) =>
          `<option value="${v}"${d.nachweis === v ? ' selected' : ''}>${n}</option>`).join('')}</select></div>
      </div>
    </div>

    <span class="dabs__lab">TEXT DER ENTSCHULDIGUNG</span>
    <textarea class="dabs__area" id="entText" maxlength="1000" placeholder="Text hier eingeben">${esc(d.text)}</textarea>

    ${stunden ? `<span class="dabs__lab">STUNDEN${a.lehrerGesamt ? ` · ${a.lehrerBearbeitet || 0}/${a.lehrerGesamt} LEHRKRÄFTE BEARBEITET` : ''}</span>
    <ul class="ent__stunden">${stunden}</ul>` : ''}

    ${darf && (a.status !== 'offen' || (a.stunden || []).some(s => s.status !== 'offen'))
      ? '<button type="button" class="ent__zurueck" id="entZurueck">Entschuldigung zurücknehmen</button>' : ''}`;

  if (!darf){
    $$('#entBody input, #entBody textarea, #entBody select, #entBody button').forEach(el => { el.disabled = true; });
    $('#entSave').hidden = true;
    return;
  }
  $('#entSave').hidden = false;
  $$('#entBody [data-st]').forEach(b => b.addEventListener('click', () => {
    entSammeln();
    entEntwurf.status = b.dataset.st;
    entZeichnen();
  }));
  const z = $('#entZurueck');
  if (z) z.addEventListener('click', async () => {
    if (await entSetzen(entOffen, 'offen')) SH.ent.close();
  });
}

$('#entClose').addEventListener('click', () => SH.ent.close());
$('#entSave').addEventListener('click', async () => {
  entSammeln();
  const a = entOffen, d = entEntwurf;
  if (!a || !d) return;
  const ok = await entSetzen(a, d.status, d.status === 'offen' ? null
    : { datum: d.datum || iso(new Date()), nachweis: d.nachweis || '', text: (d.text || '').trim() });
  if (ok) SH.ent.close();
});
if (typeof window !== 'undefined') window.UntPlanEntschuldigung = { zeitraum: entZeitraum, status: ENT_STATUS, nachweis: ENT_NACHWEIS };

/* Erziehungsberechtigte mit mehreren Kindern: Kind über der Liste wählen. */
function absKinder(d){
  const kinder = d.children || [];
  if (kinder.length < 2) return '';
  listeStand.kind = d.selected || kinder[0].id;
  return `<div class="abskind" role="group" aria-label="Kind">${kinder.map(k =>
    `<button type="button" class="lstchip${k.id === listeStand.kind ? ' is-on' : ''}" data-kind="${k.id}" aria-pressed="${k.id === listeStand.kind}">${esc(k.name)}${k.class ? ' (' + esc(k.class) + ')' : ''}</button>`).join('')}</div>`;
}

/* Eine Karte wie in der Vorlage: Datum, Uhrzeit, Anlass, Stand. */
function karteAbwesenheit(x, mitName){
  const zeit = x.timeFrom || x.timeTo ? `${esc(x.timeFrom)} - ${esc(x.timeTo)}`
                                      : `${deDate(x.from)} - ${deDate(x.to)}`;
  /* „15. Okt. - 16. Okt.“ wie im Original */
  const tagP = d => { const t = deTag(d); return /\.$/.test(t) ? t : t + '.'; };
  const anlass = [x.art, x.reason].filter(Boolean).join(' · ');
  return `
    <article class="lstcard">
      <b>${esc(mitName ? x.name : (x.to && x.to !== x.from
            ? tagP(x.from) + ' - ' + tagP(x.to)
            : tagP(x.from)))}</b>
      ${mitName ? `<p class="lstcard__sub">${esc(deDate(x.from))}${x.from !== x.to ? ' – ' + esc(deDate(x.to)) : ''}</p>` : ''}
      <p class="lstcard__line"><svg viewBox="0 0 24 24" class="lstcard__ico"><circle cx="12" cy="12" r="8.6"/><path d="M12 7.2V12l3.1 2"/></svg>${zeit}${
        x.lessons ? ` <span class="lstcard__sm">(${esc(x.lessons)})</span>` : ''}</p>
      ${anlass ? `<p class="lstcard__line"><svg viewBox="0 0 24 24" class="lstcard__ico"><circle cx="12" cy="12" r="8.6"/><path d="M12 11.2v5.4"/><circle cx="12" cy="7.9" r=".95" fill="currentColor" stroke="none"/></svg>${esc(anlass)}</p>` : ''}
      ${String(x.note || '').trim() ? `<p class="lstcard__line lstcard__line--note"><svg viewBox="0 0 24 24" class="lstcard__ico"><path d="M15.6 3.4H6.3A1.8 1.8 0 0 0 4.5 5.2v13.6a1.8 1.8 0 0 0 1.8 1.8h11.4a1.8 1.8 0 0 0 1.8-1.8V6.9z"/><path d="M7.8 8.8h7.8M7.8 12h7.8M7.8 15.2h4"/></svg><span>${esc(String(x.note).trim())}</span></p>` : ''}
      <p class="lstcard__line lstcard__stand lstcard__stand--${x.tone}">
        <svg viewBox="0 0 24 24" class="lstcard__ico">${STAND_ICO[x.tone] || ''}</svg>${esc(x.label)}${
        x.source ? ` <span class="lstcard__sm">· ${esc(x.source)}</span>` : ''}</p>
      ${mitName ? `
        <div class="lstcard__acts">
          ${x.status !== 'approved' ? `<button data-excuse="${esc(x.id)}" data-stand="approved" class="lstbtn lstbtn--ok">Entschuldigen</button>` : ''}
          ${x.status !== 'rejected' ? `<button data-excuse="${esc(x.id)}" data-stand="rejected" class="lstbtn">Ablehnen</button>` : ''}
        </div>` : ''}
    </article>`;
}

$('#btnAppointments').addEventListener('click', () => listeOeffnen('exams'));
$('#btnAbsences').addEventListener('click', () => listeOeffnen('absences'));
$('#btnClassLead').addEventListener('click', () => listeOeffnen('leadabsent'));

/* =====================================================================
   Sprechtag (Elternsprechtag)
   ---------------------------------------------------------------------
   Eintrag auf der Startseite, sobald ein Sprechtag läuft (Einladung,
   Anmeldung oder mit Terminen). Lehrkräfte sehen ihr Zeitraster — frei,
   gebucht, gesperrt —, sperren oder geben Zeitfenster frei und sagen
   Termine ab. Erziehungsberechtigte buchen freie Zeitfenster bei den
   Lehrkräften ihrer Kinder und sagen gebuchte Termine ab. Alles läuft
   über api/sprechtag (dahinter UntPlanWeb, das auch die Rechte prüft).
   ===================================================================== */
const SPT_URL = new URL('api/sprechtag', location.href).href;
const spt = { liste:null, id:0, daten:null, offen:'', eltern:false, nurLesen:false, filter:'alle', karten:[] };

/* SPRECHTAG-BEGINN (wird von den Tests ausgeführt) */
/* Status eines Sprechtags (ab Sprint 27 Feld `status`): vorbereitung ·
   angekuendigt (freigegeben, Anmeldung beginnt erst später) · anmeldung ·
   geschlossen · vorbei · abgesagt. Ältere Server: aus der Phase. */
function sptStatus(t){
  if (!t) return '';
  if (t.abgesagt) return 'abgesagt';
  const s = String(t.status || '').trim().toLowerCase().replace(/ü/g, 'ue');
  if (s.startsWith('angek')) return 'angekuendigt';
  if (s) return s;
  return t.phase === 'einladung' ? 'angekuendigt' : String(t.phase || '');
}
/* Untertitel auf der Startseite (wie iOS): angekündigt der Text des Servers
   (Datum, Zeit, Ort und Anmeldebeginn), sonst Datum und Text. */
function sptUntertitel(k){
  if (!k) return '';
  const tag = k.datumText || k.datum || '';
  if (sptStatus(k) === 'angekuendigt'){
    if (k.text) return k.text;
    const zeit = k.beginn ? (k.ende ? `${k.beginn}–${k.ende} Uhr` : k.beginn) : '';
    const teile = ['Angekündigt: ' + [tag, zeit].filter(Boolean).join(', ')];
    if (k.ort) teile.push(k.ort);
    if (k.anmeldungVonText) teile.push('Anmeldung ab ' + k.anmeldungVonText);
    return teile.join(' · ');
  }
  return [tag, k.text].filter(Boolean).join(' · ');
}
/* Zeiten des Tages: mehrere Zeitblöcke („15:00–17:00, 17:30–19:00“), sonst Beginn bis Ende. */
function sptZeiten(t){
  const b = Array.isArray(t && t.zeitbloecke) ? t.zeitbloecke : [];
  if (b.length > 1) return b.map(x => `${String(x.von || '').slice(0, 5)}–${String(x.bis || '').slice(0, 5)}`).join(', ');
  if (!t || !t.beginn) return '';
  return t.ende ? `${String(t.beginn).slice(0, 5)}–${String(t.ende).slice(0, 5)}` : String(t.beginn).slice(0, 5);
}
if (typeof window !== 'undefined') window.UntPlanSprechtag = { sptStatus, sptUntertitel, sptZeiten };
/* SPRECHTAG-ENDE */

async function sptFetch(pfad, methode, body){
  const r = await fetch(SPT_URL + pfad, {
    method: methode || 'GET', credentials:'same-origin', cache:'no-store',
    headers: body ? { 'Content-Type':'application/json' } : {},
    body: body ? JSON.stringify(body) : undefined
  });
  const d = await r.json().catch(() => null);
  if (!r.ok) throw new Error((d && (d.error || d.fehler)) || ('HTTP ' + r.status));
  return d;
}

const sptZeit = t => String(t || '').slice(0, 5);
const sptKind = k => k ? [k.vorname, k.name].filter(Boolean).join(' ') + (k.klasse ? ` (${k.klasse})` : '') : '';

/* Startseite: Eintrag nur zeigen, wenn es etwas zu tun oder zu sehen gibt –
   auch angekündigte Sprechtage (noch keine Buchung möglich) mit Datum, Ort
   und Anmeldebeginn. Die Karten kommen mit der Startseite (aus /heute) oder
   einzeln über api/sprechtag/start. */
const SPT_MEGAFON = '<svg viewBox="0 0 24 24" class="ico"><path d="M4 10.2v3.6a1 1 0 0 0 1 1h2.4l6.6 4V5.2l-6.6 4H5a1 1 0 0 0-1 1z"/><path d="M17.2 9.2a4 4 0 0 1 0 5.6M19.6 7a7.2 7.2 0 0 1 0 10"/><path d="m8.2 14.8.9 4.4"/></svg>';
let sptIcoNormal = null;
function sptStartZeigen(karten){
  const row = $('#btnSprechtag');
  if (!row) return;
  spt.karten = Array.isArray(karten) ? karten : [];
  if (!spt.karten.length){ row.hidden = true; return; }
  const k = spt.karten[0];
  const ico = row.querySelector('.row__ico');
  if (ico){
    if (sptIcoNormal === null) sptIcoNormal = ico.innerHTML;
    ico.innerHTML = sptStatus(k) === 'angekuendigt' ? SPT_MEGAFON : sptIcoNormal;
  }
  row.classList.toggle('is-angekuendigt', sptStatus(k) === 'angekuendigt');
  $('#sptSub').textContent = sptUntertitel(k);
  row.hidden = false;
}
async function sptStart(karten){
  const row = $('#btnSprechtag');
  if (!row || state.source !== 'db') return;
  if (Array.isArray(karten)) return sptStartZeigen(karten);
  try {
    sptStartZeigen(await sptFetch('/start'));
  } catch (e){ row.hidden = true; }
}
$('#btnSprechtag').addEventListener('click', () => listeOeffnen('sprechtag'));

async function sprechtagZeichnen(){
  const body = $('#lstBody');
  $('#lstTitle').textContent = 'Sprechtag';
  $('#lstRange').textContent = '';
  if (!body.querySelector('.spt')) body.innerHTML = '<div class="spt"><div class="spt__laden" aria-label="Wird geladen"></div></div>';
  try {
    if (!spt.liste) spt.liste = await sptFetch('');
  } catch (e){
    body.innerHTML = `<div class="lst__empty"><b>Nicht erreichbar</b><span>${esc(e.message)}</span></div>`;
    return;
  }
  /* Familiensicht: Erziehungsberechtigte und Schüler*innen (diese nur lesend). */
  spt.eltern = !!(spt.liste.eltern || spt.liste.schueler);
  spt.nurLesen = !spt.liste.eltern && !!spt.liste.schueler;
  /* Angekündigte Sprechtage sehen auch nicht beteiligte Lehrkräfte. */
  const tage = (spt.liste.sprechtage || [])
    .filter(x => spt.eltern || x.beteiligt || sptStatus(x.sprechtag) === 'angekuendigt')
    .filter(x => x.sprechtag && x.sprechtag.phase !== 'vorbereitung');
  if (!tage.length){ body.innerHTML = LEER('Kein Sprechtag', 'Derzeit ist kein Sprechtag geplant.'); return; }
  if (!spt.id && tage.length === 1) spt.id = tage[0].sprechtag.id;
  if (!spt.id){
    /* Mehrere Sprechtage: erst auswählen — gruppierte Liste wie in iOS. */
    body.innerHTML = `<div class="spt"><p class="spt__gruppe">Sprechtage</p><div class="spt__liste">${tage.map(x => `
      <button class="spt__tag" data-id="${x.sprechtag.id}" type="button">
        <span class="spt__tagDatum"><b>${esc(sptTagZahl(x.sprechtag.datum))}</b><i>${esc(sptMonat(x.sprechtag.datum))}</i></span>
        <span class="spt__was"><b>${esc(x.sprechtag.name)}</b>
          <span>${esc(x.sprechtag.datumText)} · ${esc(sptZeiten(x.sprechtag))} Uhr${x.sprechtag.ort ? ' · ' + esc(x.sprechtag.ort) : ''}</span>
          <span>${x.termine} ${x.termine === 1 ? 'Termin' : 'Termine'} · ${esc(SPT_STATUS[sptStatus(x.sprechtag)] || SPT_PHASE[x.sprechtag.phase] || x.sprechtag.phase)}</span></span>
        <svg viewBox="0 0 24 24" class="ico spt__chev"><path d="m10 6 6 6-6 6"/></svg>
      </button>`).join('')}</div></div>`;
    $$('#lstBody .spt__tag').forEach(b => b.addEventListener('click', () => { spt.id = +b.dataset.id; spt.filter = 'alle'; sprechtagZeichnen(); }));
    return;
  }
  /* Nicht beteiligte Lehrkraft: nur die Ankündigung (keine eigenen Zeiten). */
  const eintrag = tage.find(x => x.sprechtag.id === spt.id);
  if (!spt.eltern && eintrag && !eintrag.beteiligt){
    $('#lstTitle').textContent = eintrag.sprechtag.name || 'Sprechtag';
    $('#lstRange').textContent = eintrag.sprechtag.datumText || '';
    body.innerHTML = `<div class="spt">${sptKopf(eintrag.sprechtag)}</div>`;
    return;
  }
  try {
    spt.daten = await sptFetch(`/${spt.id}/${spt.eltern ? 'eltern' : 'lehrkraft'}`);
  } catch (e){
    /* Ohne Details, aber mit Startkarte (z. B. angekündigt): die Ankündigung statt des Fehlers. */
    const karte = (spt.karten || []).find(k => k.id === spt.id);
    body.innerHTML = karte ? `<div class="spt">${sptKarteKopf(karte)}</div>`
      : `<div class="lst__empty"><b>Nicht verfügbar</b><span>${esc(e.message)}</span></div>`;
    return;
  }
  const t = spt.daten.sprechtag || {};
  $('#lstTitle').textContent = t.name || 'Sprechtag';
  $('#lstRange').textContent = t.datumText || '';
  spt.eltern ? sptElternZeichnen() : sptLehrkraftZeichnen();
}

const SPT_PHASE = { einladung:'Einladungsphase', anmeldung:'Anmeldung läuft', geschlossen:'Anmeldung geschlossen',
                    vorbei:'Vorbei', abgesagt:'Abgesagt', vorbereitung:'In Vorbereitung' };
const SPT_STATUS = { angekuendigt:'Angekündigt', anmeldung:'Anmeldung läuft', geschlossen:'Anmeldung geschlossen',
                     vorbei:'Vorbei', abgesagt:'Abgesagt', vorbereitung:'In Vorbereitung' };
/* Hinweis zur Ankündigung, z. B. „Angekündigt – Anmeldung ab Mo, 02.11.2026 08:00“. */
const sptAnkuendigung = t => t.statusText || (t.anmeldungVonText ? `Angekündigt – Anmeldung ab ${t.anmeldungVonText}`
                                                                  : 'Angekündigt – die Anmeldung ist noch nicht geöffnet.');
const SPT_MONATE = ['Jan.', 'Feb.', 'März', 'Apr.', 'Mai', 'Juni', 'Juli', 'Aug.', 'Sep.', 'Okt.', 'Nov.', 'Dez.'];
const sptTagZahl = d => { const m = /^\d{4}-(\d{2})-(\d{2})/.exec(d || ''); return m ? String(+m[2]) : ''; };
const sptMonat = d => { const m = /^\d{4}-(\d{2})/.exec(d || ''); return m ? SPT_MONATE[+m[1] - 1] || '' : ''; };
const sptKuerzel = n => String(n || '').split(/\s+/).filter(Boolean).slice(-2).map(w => w[0]).join('').toUpperCase();
const SPT_ICO = {
  uhr:  '<svg viewBox="0 0 24 24" class="ico"><circle cx="12" cy="12" r="8.6"/><path d="M12 7.2V12l3.1 2"/></svg>',
  raum: '<svg viewBox="0 0 24 24" class="ico"><path d="M5 20V5.6A1.6 1.6 0 0 1 6.6 4h10.8A1.6 1.6 0 0 1 19 5.6V20M3 20h18"/><circle cx="15" cy="12.5" r=".9" fill="currentColor" stroke="none"/></svg>',
  sperr:'<svg viewBox="0 0 24 24" class="ico"><rect x="5.5" y="10.5" width="13" height="9.5" rx="2"/><path d="M8.5 10.5V8a3.5 3.5 0 0 1 7 0v2.5"/></svg>',
  frei: '<svg viewBox="0 0 24 24" class="ico"><rect x="5.5" y="10.5" width="13" height="9.5" rx="2"/><path d="M8.5 10.5V8a3.5 3.5 0 0 1 6.8-1.2"/></svg>',
  weg:  '<svg viewBox="0 0 24 24" class="ico"><circle cx="12" cy="12" r="8.6"/><path d="m9 9 6 6M15 9l-6 6"/></svg>',
  kal:  '<svg viewBox="0 0 24 24" class="ico"><rect x="4.2" y="5" width="15.6" height="14.5" rx="2"/><path d="M4.2 9.4h15.6M8.2 3.2v3.4M15.8 3.2v3.4"/></svg>',
  ort:  '<svg viewBox="0 0 24 24" class="ico"><path d="M12 20.5s-6-5.6-6-10.3a6 6 0 0 1 12 0c0 4.7-6 10.3-6 10.3z"/><circle cx="12" cy="10.2" r="2.1"/></svg>',
  video:'<svg viewBox="0 0 24 24" class="ico"><rect x="3.5" y="6.5" width="12" height="11" rx="2"/><path d="m15.5 10.6 5-3v8.8l-5-3"/></svg>'
};

/* Bestätigung als Aktionsblatt wie in iOS: Gruppe mit Titel, Text,
   optionalem Eingabefeld und den Knöpfen; darunter abgesetzt „Abbrechen“.
   Liefert { wert, text } oder null. */
function sptBlatt({ titel = '', text = '', eingabe = null, knoepfe = [] }){
  return new Promise(fertig => {
    const el = document.createElement('div');
    el.className = 'iosas';
    el.innerHTML = `<div class="iosas__scrim"></div>
      <div class="iosas__panel" role="alertdialog" aria-modal="true" aria-label="${esc(titel)}">
        <div class="iosas__group">
          ${titel || text ? `<div class="iosas__head">${titel ? `<b>${esc(titel)}</b>` : ''}${text ? `<span>${esc(text)}</span>` : ''}</div>` : ''}
          ${eingabe ? `<div class="iosas__feld"><textarea rows="2" maxlength="300" placeholder="${esc(eingabe)}"></textarea></div>` : ''}
          ${knoepfe.map((k, i) => `<button type="button" class="iosas__btn${k.rot ? ' is-rot' : ''}${k.fett ? ' is-fett' : ''}" data-i="${i}">${esc(k.label)}</button>`).join('')}
        </div>
        <button type="button" class="iosas__btn iosas__btn--abbr">Abbrechen</button>
      </div>`;
    document.body.appendChild(el);
    requestAnimationFrame(() => el.classList.add('is-open'));
    const zu = wert => {
      el.classList.remove('is-open');
      setTimeout(() => el.remove(), 300);
      fertig(wert);
    };
    el.querySelector('.iosas__scrim').addEventListener('click', () => zu(null));
    el.querySelector('.iosas__btn--abbr').addEventListener('click', () => zu(null));
    el.querySelectorAll('[data-i]').forEach(b => b.addEventListener('click', () => {
      const ta = el.querySelector('textarea');
      zu({ wert: knoepfe[+b.dataset.i].wert, text: ta ? ta.value.trim() : '' });
    }));
    const ta = el.querySelector('textarea');
    if (ta) setTimeout(() => { try { ta.focus({ preventScroll:true }); } catch (e){} }, 320);
  });
}

/* Zusammenfassung oben: Name, Zeit, Raum, Phase und Hinweis. */
function sptKopf(t, raum){
  return `<div class="spt__kopf">
      <div class="spt__kopfZeile">
        <span class="spt__kal"><i>${esc(sptMonat(t.datum))}</i><b>${esc(sptTagZahl(t.datum))}</b></span>
        <span class="spt__kopfText">
          <b>${esc(t.name)}</b>
          <span>${SPT_ICO.uhr}${esc(sptZeiten(t))} Uhr${t.dauer ? ` · je ${esc(t.dauer)} Min.` : ''}</span>
          ${raum ? `<span>${SPT_ICO.raum}Raum ${esc(raum)}</span>` : ''}
          ${t.ort ? `<span>${SPT_ICO.ort}${esc(t.ort)}</span>` : ''}
        </span>
      </div>
      <span class="spt__phase spt__phase--${esc(t.phase)} spt__phase--${esc(sptStatus(t))}"><i></i>${esc(SPT_STATUS[sptStatus(t)] || SPT_PHASE[t.phase] || t.phase)}</span>
      ${t.abgesagt ? `<p class="spt__hinweis spt__hinweis--rot">Der Sprechtag wurde abgesagt.${t.absageGrund ? ' Grund: ' + esc(t.absageGrund) : ''}</p>`
        : sptStatus(t) === 'angekuendigt' ? `<p class="spt__hinweis spt__hinweis--ankuendigung">${SPT_MEGAFON}<span>${esc(sptAnkuendigung(t))}</span></p>`
        : ''}
      ${!t.abgesagt && t.hinweis ? `<p class="spt__hinweis">${esc(t.hinweis)}</p>` : ''}
    </div>`;
}

/* Ankündigung aus der Startkarte (ohne Details). */
function sptKarteKopf(k){
  return sptKopf({ name:k.name, datum:k.datum, datumText:k.datumText, beginn:k.beginn, ende:k.ende, ort:k.ort,
                   phase:k.phase, status:k.status, statusText: sptStatus(k) === 'angekuendigt' ? (k.text || '') : '',
                   anmeldungVonText:k.anmeldungVonText });
}

/* Zeitfenster in Blöcke teilen: Wo eine Pause liegt, beginnt ein neuer Abschnitt. */
function sptBloecke(fenster){
  const res = [];
  fenster.forEach(f => {
    const letzt = res[res.length - 1];
    if (letzt && sptZeit(letzt[letzt.length - 1].ende) === sptZeit(f.beginn)) letzt.push(f);
    else res.push([f]);
  });
  return res;
}

/* Wischen nach links legt die Aktion frei; Tippen öffnet das Aktionsblatt. */
function sptWischen(zeile, beimTippen){
  const vorn = zeile.querySelector('.spt-row__front');
  const breite = () => (zeile.querySelector('.spt-row__acts') || {}).offsetWidth || 0;
  let x0 = 0, y0 = 0, dx = 0, start = 0, aktiv = false, quer = null;
  const setze = (x, weich) => { vorn.style.transition = weich ? '' : 'none'; vorn.style.transform = x ? `translateX(${x}px)` : ''; };
  vorn.addEventListener('touchstart', e => {
    $$('#lstBody .spt-row.is-auf').forEach(r => { if (r !== zeile){ r.classList.remove('is-auf'); r.querySelector('.spt-row__front').style.transform = ''; } });
    x0 = e.touches[0].clientX; y0 = e.touches[0].clientY; dx = 0; quer = null; aktiv = !!breite();
    start = zeile.classList.contains('is-auf') ? -breite() : 0;
  }, { passive:true });
  vorn.addEventListener('touchmove', e => {
    if (!aktiv) return;
    const mx = e.touches[0].clientX - x0, my = e.touches[0].clientY - y0;
    if (quer === null && (Math.abs(mx) > 8 || Math.abs(my) > 8)) quer = Math.abs(mx) > Math.abs(my);
    if (!quer) return;
    dx = mx;
    setze(Math.min(0, Math.max(-breite() - 24, start + dx)), false);
  }, { passive:true });
  vorn.addEventListener('touchend', () => {
    if (!aktiv || !quer) return;
    const auf = start + dx < -breite() / 2;
    zeile.classList.toggle('is-auf', auf);
    setze(auf ? -breite() : 0, true);
    zeile.dataset.gewischt = '1';
    setTimeout(() => { delete zeile.dataset.gewischt; }, 350);
  }, { passive:true });
  vorn.addEventListener('click', () => {
    if (zeile.dataset.gewischt) return;
    if (zeile.classList.contains('is-auf')){ zeile.classList.remove('is-auf'); setze(0, true); return; }
    beimTippen();
  });
}

/* Lehrkraft: Segmentschalter Alle/Gebucht/Frei, darunter die Zeitfenster
   als gruppierte Liste. Tippen oder Wischen: Sperren, Freigeben, Absagen. */
function sptLehrkraftZeichnen(){
  const d = spt.daten, t = d.sprechtag, l = d.lehrer || {};
  const fenster = d.zeitfenster || [];
  const zahl = st => fenster.filter(f => f.status === st).length;
  const vorbei = t.phase === 'vorbei' || t.phase === 'abgesagt';
  const aktiv = !vorbei && !l.abgesagt;
  const filter = spt.filter || 'alle';
  const sichtbar = f => filter === 'alle' || f.status === filter;
  const bloecke = sptBloecke(fenster).map(b => b.filter(sichtbar)).filter(b => b.length);

  const zeile = f => {
    const x = f.termin;
    const name = f.status === 'gebucht' && x ? esc(sptKind(x.schueler) || 'Gebucht')
      : f.status === 'gesperrt' ? 'Gesperrt' : 'Frei';
    const unter = f.status === 'gebucht' && x
      ? [x.gebuchtVon ? 'gebucht von ' + x.gebuchtVon : '', x.bemerkung || ''].filter(Boolean).map(esc).join(' · ')
      : f.status === 'gesperrt' ? 'Nicht buchbar' : 'Noch buchbar';
    const akt = !aktiv ? '' : f.status === 'gebucht'
      ? `<button class="spt-row__act is-rot" data-absagen="${x ? x.id : ''}" type="button">${SPT_ICO.weg}<span>Absagen</span></button>`
      : f.status === 'gesperrt'
      ? `<button class="spt-row__act is-gruen" data-sperren="0" type="button">${SPT_ICO.frei}<span>Freigeben</span></button>`
      : `<button class="spt-row__act is-grau" data-sperren="1" type="button">${SPT_ICO.sperr}<span>Sperren</span></button>`;
    return `<div class="spt-row spt-row--${f.status}" data-beginn="${esc(f.beginn)}">
        ${akt ? `<div class="spt-row__acts">${akt}</div>` : ''}
        <div class="spt-row__front" role="button" tabindex="0">
          <span class="spt__zeit"><b>${esc(sptZeit(f.beginn))}</b><small>bis ${esc(sptZeit(f.ende))}</small></span>
          <span class="spt__punkt" aria-hidden="true"></span>
          <span class="spt__was"><b>${name}</b><span>${unter}</span></span>
          ${aktiv ? '<svg viewBox="0 0 24 24" class="ico spt__chev"><path d="m10 6 6 6-6 6"/></svg>' : ''}
        </div>
      </div>`;
  };

  const leer = { gebucht:['Noch keine Termine', 'Sobald Eltern einen Termin buchen, erscheint er hier.'],
                 frei:['Keine freien Zeiten', 'Alle Zeitfenster sind gebucht oder gesperrt.'],
                 alle:['Keine Zeitfenster', 'Für diesen Sprechtag sind keine Zeiten eingeteilt.'] }[filter];

  $('#lstBody').innerHTML = `<div class="spt">
    ${sptKopf(t, l.raum)}
    ${l.abgesagt ? `<p class="spt__hinweis spt__hinweis--rot spt__hinweis--frei">Sie haben Ihre Teilnahme abgesagt.${l.absageGrund ? ' Grund: ' + esc(l.absageGrund) : ''}</p>` : ''}
    <div class="spt__seg" role="tablist">
      ${[['alle', 'Alle', fenster.length], ['gebucht', 'Gebucht', zahl('gebucht')], ['frei', 'Frei', zahl('frei')]].map(([k, n, z]) =>
        `<button type="button" role="tab" class="${filter === k ? 'is-on' : ''}" aria-selected="${filter === k}" data-filter="${k}">${n} <i>${z}</i></button>`).join('')}
    </div>
    ${bloecke.length ? bloecke.map(b => `
      <p class="spt__gruppe">${esc(sptZeit(b[0].beginn))} – ${esc(sptZeit(b[b.length - 1].ende))} Uhr<span>${b.length} ${b.length === 1 ? 'Termin' : 'Termine'}</span></p>
      <div class="spt__liste">${b.map(zeile).join('')}</div>`).join('')
      : `<div class="spt__leerBox">${SPT_ICO.kal}<b>${leer[0]}</b><span>${leer[1]}</span></div>`}
    ${aktiv && bloecke.length ? `<p class="spt__fuss">Zum Sperren, Freigeben oder Absagen auf ein Zeitfenster tippen oder nach links wischen.${zahl('gesperrt') ? ` ${zahl('gesperrt')} gesperrt.` : ''}</p>` : ''}
  </div>`;

  $$('#lstBody .spt__seg button').forEach(b => b.addEventListener('click', () => {
    spt.filter = b.dataset.filter; buzz(); sptLehrkraftZeichnen();
  }));
  const sperren = async (beginn, an) => {
    try {
      await sptFetch(`/${spt.id}/sperren`, 'PUT', { beginne:[beginn], gesperrt: an });
      buzz();
      toast(an ? `${sptZeit(beginn)} Uhr gesperrt` : `${sptZeit(beginn)} Uhr freigegeben`);
      await sprechtagZeichnen();
    } catch (e){ toast('Nicht gespeichert: ' + e.message); }
  };
  $$('#lstBody .spt-row').forEach(r => {
    const f = fenster.find(x => x.beginn === r.dataset.beginn);
    if (!f) return;
    if (!aktiv){ return; }
    sptWischen(r, async () => {
      const zeit = `${sptZeit(f.beginn)}–${sptZeit(f.ende)} Uhr`;
      if (f.status === 'gebucht') return sptAbsagen(f.termin && f.termin.id, zeit, sptKind(f.termin && f.termin.schueler));
      const sperr = f.status !== 'gesperrt';
      const a = await sptBlatt({ titel: zeit, text: sperr ? 'Dieses Zeitfenster sperren? Eltern können es dann nicht mehr buchen.' : 'Dieses Zeitfenster wieder zur Buchung freigeben?',
        knoepfe: [{ label: sperr ? 'Zeitfenster sperren' : 'Zeitfenster freigeben', wert: 1, fett: !sperr }] });
      if (a) sperren(f.beginn, sperr);
    });
    r.querySelectorAll('[data-sperren]').forEach(b => b.addEventListener('click', () => sperren(f.beginn, b.dataset.sperren === '1')));
    r.querySelectorAll('[data-absagen]').forEach(b => b.addEventListener('click', () =>
      sptAbsagen(+b.dataset.absagen, `${sptZeit(f.beginn)}–${sptZeit(f.ende)} Uhr`, sptKind(f.termin && f.termin.schueler))));
  });
}

async function sptAbsagen(tid, zeit, wer){
  if (!tid) return;
  const a = await sptBlatt({ titel: 'Termin absagen?', text: [zeit, wer].filter(Boolean).join(' · ') + '\nDie Absage wird mitgeteilt.',
    eingabe: 'Grund (optional)', knoepfe: [{ label: 'Termin absagen', wert: 1, rot: true }] });
  if (!a) return;
  try {
    await sptFetch(`/${spt.id}/termine/${tid}/absagen`, 'POST', { grund: a.text });
    buzz();
    toast('Termin abgesagt');
    spt.liste = null;
    await sprechtagZeichnen();
    sptStart();
  } catch (e){ toast('Nicht möglich: ' + e.message); }
}

/* Eltern: eigene Termine oben, darunter je Kind eine Karte je Lehrkraft
   mit den freien Zeiten als Chips. */
function sptElternZeichnen(){
  const d = spt.daten, t = d.sprechtag;
  const termine = (d.termine || []).slice().sort((a, b) => String(a.beginn).localeCompare(String(b.beginn)));
  const nurLesen = spt.nurLesen || !!d.nurLesen;
  const absagbar = !nurLesen && t.phase !== 'vorbei' && t.phase !== 'abgesagt' && sptStatus(t) !== 'vorbei';
  const angekuendigt = sptStatus(t) === 'angekuendigt';
  /* Angekündigt: die freien Zeiten der Lehrkräfte als Vorschau (nicht buchbar). */
  const vorschau = !d.buchbar && angekuendigt && !nurLesen;
  const html = [`<div class="spt">${sptKopf(t)}`];
  if (nurLesen)
    html.push('<p class="spt__hinweis spt__hinweis--frei">Termine buchen Ihre Erziehungsberechtigten.</p>');
  else if (!d.buchbar && t.phase !== 'vorbei' && t.phase !== 'abgesagt')
    html.push(`<p class="spt__hinweis spt__hinweis--frei">${angekuendigt && t.anmeldungVonText ? `Termine können ab ${esc(t.anmeldungVonText)} gebucht werden. Die Zeiten unten sind eine Vorschau.`
      : (t.phase === 'einladung' || angekuendigt) ? 'Die Anmeldung hat noch nicht begonnen. Freie Zeiten erscheinen hier, sobald sie startet.' : 'Die Anmeldung ist geschlossen.'}</p>`);
  else if (d.buchbar && d.maxTermine > 0)
    html.push(`<p class="spt__hinweis spt__hinweis--frei">Höchstens ${esc(d.maxTermine)} Termine je Familie${d.gebuchtFamilie ? ` · ${esc(d.gebuchtFamilie)} gebucht` : ''}.</p>`);

  html.push(`<p class="spt__gruppe">Meine Termine<span>${termine.length || ''}</span></p>`);
  html.push(termine.length ? `<div class="spt__liste">${termine.map(x => `
      <div class="spt-row spt-row--gebucht" data-tid="${x.id}">
        ${absagbar ? `<div class="spt-row__acts"><button class="spt-row__act is-rot" data-absagen="${x.id}" type="button">${SPT_ICO.weg}<span>Absagen</span></button></div>` : ''}
        <div class="spt-row__front" role="button" tabindex="0">
          <span class="spt__zeit"><b>${esc(sptZeit(x.beginn))}</b><small>bis ${esc(sptZeit(x.ende))}</small></span>
          <span class="spt__punkt" aria-hidden="true"></span>
          <span class="spt__was"><b>${esc(x.lehrerName || x.lehrer)}</b><span>${esc([sptKind(x.schueler), x.online ? 'Online' : x.raum ? 'Raum ' + x.raum : ''].filter(Boolean).join(' · '))}</span>${
            x.online && /^https?:\/\//i.test(x.link || '') ? `<a class="spt__link" href="${esc(x.link)}" target="_blank" rel="noopener noreferrer">${SPT_ICO.video}Online-Termin öffnen</a>` : ''}</span>
          ${absagbar ? '<svg viewBox="0 0 24 24" class="ico spt__chev"><path d="m10 6 6 6-6 6"/></svg>' : ''}
        </div>
      </div>`).join('')}</div>`
    : `<div class="spt__leerBox spt__leerBox--klein">${SPT_ICO.kal}<b>Noch keine Termine</b><span>${d.buchbar && !nurLesen ? 'Tippen Sie unten auf eine freie Zeit, um einen Termin zu buchen.' : 'Es sind keine Termine gebucht.'}</span></div>`);

  (d.kinder || []).forEach(k => {
    html.push(`<p class="spt__gruppe">${esc(sptKind(k))}</p>`);
    const lk = k.lehrkraefte || [];
    if (!lk.length) html.push('<div class="spt__leerBox spt__leerBox--klein"><b>Keine Lehrkräfte</b><span>Für dieses Kind nimmt keine Lehrkraft teil.</span></div>');
    lk.forEach(l => {
      const fenster = l.zeitfenster || [];
      const zusatz = [(l.faecher || []).join(', '), l.klassenleitung ? 'Klassenleitung' : '',
                      l.online ? 'Online' : l.raum ? 'Raum ' + l.raum : '', l.dauer > 0 ? l.dauer + ' Min.' : ''].filter(Boolean).join(' · ');
      const status = l.abgesagt ? '<span class="spt__badge spt__badge--grau">nimmt nicht teil</span>'
        : l.termin ? `<span class="spt__badge spt__badge--gebucht">${esc(sptZeit(l.termin.beginn))} Uhr</span>`
        : l.ausgebucht ? '<span class="spt__badge spt__badge--grau">Ausgebucht</span>'
        : d.buchbar && !nurLesen ? `<span class="spt__badge spt__badge--frei">${fenster.filter(f => !f.konflikt).length} frei</span>` : '';
      let unten = '';
      if (l.abgesagt) unten = '<p class="spt__karteText">Diese Lehrkraft nimmt am Sprechtag nicht teil.</p>';
      else if (l.termin) unten = `<p class="spt__karteText spt__karteText--ok">${SPT_ICO.uhr}Termin um ${esc(sptZeit(l.termin.beginn))} Uhr gebucht</p>`;
      else if (vorschau) unten = fenster.length
        ? `<div class="spt__slots spt__slots--vorschau" aria-label="Vorschau">${fenster.map(f => `<span class="spt__slot is-vorschau">${esc(sptZeit(f.beginn))}</span>`).join('')}</div>`
        : '';
      else if (d.buchbar && !nurLesen) unten = fenster.length
        ? `<div class="spt__slots">${fenster.map(f =>
            `<button class="spt__slot${f.konflikt ? ' is-konflikt' : ''}" type="button" data-kind="${k.id}" data-lehrer="${esc(l.kurz)}" data-name="${esc(l.name || l.kurz)}" data-beginn="${esc(f.beginn)}"
               ${f.konflikt ? 'title="Überschneidet sich mit einem anderen Termin"' : ''}>${esc(sptZeit(f.beginn))}</button>`).join('')}</div>`
        : '<p class="spt__karteText">Keine freien Zeiten mehr.</p>';
      html.push(`<div class="spt__karte${l.abgesagt ? ' is-aus' : ''}">
          <div class="spt__karteKopf">
            <span class="spt__ava">${esc(sptKuerzel(l.name || l.kurz))}</span>
            <span class="spt__was"><b>${esc(l.name || l.kurz)}${l.eingeladen ? ' <i class="spt__einl">eingeladen</i>' : ''}</b>${zusatz ? `<span>${esc(zusatz)}</span>` : ''}</span>
            ${status}
          </div>
          ${l.eingeladen && l.einladungText ? `<p class="spt__hinweis">${esc(l.einladungText)}</p>` : ''}
          ${unten}
        </div>`);
    });
  });
  html.push('</div>');
  $('#lstBody').innerHTML = html.join('');

  $$('#lstBody .spt-row[data-tid]').forEach(r => {
    const x = termine.find(y => String(y.id) === r.dataset.tid);
    if (!x || !absagbar) return;
    const zeit = `${sptZeit(x.beginn)}–${sptZeit(x.ende)} Uhr`, wer = x.lehrerName || x.lehrer;
    sptWischen(r, () => sptAbsagen(x.id, zeit, wer));
    r.querySelectorAll('[data-absagen]').forEach(b => b.addEventListener('click', () => sptAbsagen(x.id, zeit, wer)));
  });
  $$('#lstBody .spt__slot').forEach(b => b.addEventListener('click', async () => {
    const kind = (d.kinder || []).find(k => String(k.id) === b.dataset.kind);
    const a = await sptBlatt({
      titel: `${sptZeit(b.dataset.beginn)} Uhr bei ${b.dataset.name}`,
      text: [kind ? 'Für ' + sptKind(kind) : '', b.classList.contains('is-konflikt') ? 'Achtung: Diese Zeit überschneidet sich mit einem anderen Termin.' : ''].filter(Boolean).join('\n'),
      knoepfe: [{ label: 'Termin buchen', wert: 1, fett: true }] });
    if (!a) return;
    try {
      await sptFetch(`/${spt.id}/buchen`, 'POST', { lehrer: b.dataset.lehrer, schuelerId: +b.dataset.kind, beginn: b.dataset.beginn });
      buzz();
      toast(`Termin um ${sptZeit(b.dataset.beginn)} Uhr gebucht`);
      spt.liste = null;
      await sprechtagZeichnen();
      sptStart();
    } catch (e){ toast('Nicht gebucht: ' + e.message); }
  }));
}
$('#lstBack').addEventListener('click', () => {
  /* Sprechtag mit mehreren Terminen: erst zurück zur Auswahl. */
  if (listeStand.art === 'sprechtag' && spt.id && spt.liste
      && (spt.liste.sprechtage || []).filter(x => spt.eltern || x.beteiligt || sptStatus(x.sprechtag) === 'angekuendigt').length > 1){
    spt.id = 0; spt.offen = ''; sprechtagZeichnen(); return;
  }
  if (listeStand.art === 'sprechtag'){ spt.id = 0; spt.offen = ''; spt.liste = null; spt.filter = 'alle'; }
  showScreen('start');
});

/* Suche in der Liste: filtert die bereits geladenen Einträge. */
let lstSuche = '';
$('#lstSearch').addEventListener('input', e => {
  lstSuche = e.target.value.trim().toLowerCase();
  listeZeichnen();
});

/* Die vier neuen Zeilen auf der Profilseite. */
[['#btnContact', 'Kontaktdaten'], ['#btnPassword', 'Passwort ändern'],
 ['#btnNews', 'Was gibt es Neues'], ['#btnRate', 'App bewerten']]
  .forEach(([sel, name]) => {
    const el = $(sel);
    if (el) el.addEventListener('click', () => toast(name + ' folgt.'));
  });
/* Filter zum Antippen statt zweier Eingabeaufforderungen. */
const LST_RANGE = [
  { id:'woche', name:'Diese Woche' }, { id:'monat', name:'Dieser Monat' },
  { id:'halb',  name:'Halbjahr' },    { id:'jahr',  name:'Schuljahr' },
  { id:'alles', name:'Alles' }
];
const LST_SCOPE = [
  { id:'mine', name:'Meine Klassen' }, { id:'all', name:'Alle Klassen' }
];
const LST_STATE_LEAD = [
  { id:'offen', name:'Offen' }, { id:'ok', name:'Abgeschlossen' }, { id:'alle', name:'Alle' }
];
const LST_STATE = [
  { id:'alle', name:'Alle' }, { id:'offen', name:'Offen' }, { id:'ok', name:'Entschuldigt' }
];

function zeitraumSetzen(id){
  const h = new Date();
  if (id === 'woche'){
    const mo = mondayOf(h);
    listeStand.von = iso(mo); listeStand.bis = iso(addDays(mo, 6));
  } else if (id === 'monat'){
    listeStand.von = iso(new Date(h.getFullYear(), h.getMonth(), 1));
    listeStand.bis = iso(new Date(h.getFullYear(), h.getMonth() + 1, 0));
  } else if (id === 'halb'){
    const erstes = h.getMonth() >= 1 && h.getMonth() <= 6;
    listeStand.von = erstes ? `${h.getFullYear()}-02-01` : `${h.getFullYear()}-08-01`;
    listeStand.bis = erstes ? `${h.getFullYear()}-07-31` : `${h.getFullYear() + 1}-01-31`;
  } else if (id === 'jahr'){
    const j = h.getMonth() >= 7 ? h.getFullYear() : h.getFullYear() - 1;
    listeStand.von = `${j}-08-01`; listeStand.bis = `${j + 1}-07-31`;
  } else { listeStand.von = '2000-01-01'; listeStand.bis = '2099-12-31'; }
  listeStand.zeitraum = id;
}

function filterZeichnen(){
  const r = LST_RANGE.find(v => v.id === (listeStand.zeitraum || 'jahr')) || LST_RANGE[3];
  /* In der Vorlage heißt es „Aktuelles Schuljahr", nicht nur „Schuljahr". */
  $('#fRangeVal').textContent = r.id === 'jahr' ? 'Aktuelles Schuljahr' : r.name;
  $('#fOpenTog').classList.toggle('is-on', (listeStand.stand || 'alle') === 'offen');
  $('#fOpenRow').style.display = listeStand.art === 'exams' ? 'none' : '';

  const pick = $('#fRangePick');
  pick.innerHTML = LST_RANGE.map(v =>
    `<button class="frow frow--sub ${v.id === (listeStand.zeitraum || 'jahr') ? 'is-on' : ''}"
             data-v="${v.id}"><span class="frow__lab">${esc(v.name)}</span></button>`).join('');
  $$('#fRangePick .frow').forEach(b => b.addEventListener('click', () => {
    zeitraumSetzen(b.dataset.v);
    pick.hidden = true;
    filterZeichnen(); listeZeichnen();
  }));
}

$('#fRange').addEventListener('click', () => {
  const pick = $('#fRangePick');
  pick.hidden = !pick.hidden;
});
$('#fOpenRow').addEventListener('click', () => {
  listeStand.stand = (listeStand.stand === 'offen') ? 'alle' : 'offen';
  filterZeichnen(); listeZeichnen();
});

$('#lstFilter').addEventListener('click', () => {
  const bar = $('#lstFilterBar');
  bar.hidden = !bar.hidden;
  if (!bar.hidden) filterZeichnen();
});

/* Klassenleitung nur zeigen, wenn es eine gibt. */
(async () => {
  try {
    const d = await homeFetch('home');
    /* Sprechtage kommen mit der Startseite (aus /heute); ältere Server: einzeln. */
    sptStart(Array.isArray(d.sprechtage) ? d.sprechtage : undefined);
    if (d.school) $('#startSchool').textContent = d.school;
    newsZeichnen(d.news || []);

    /* Zusammenfassung der eigenen Fehlzeiten unter „Meine Abwesenheiten". */
    const f = d.absence || {};
    const sub = $('#absSub');
    if (sub){
      if (!f.total) sub.textContent = 'Keine im Zeitraum';
      else {
        const teile = [`${f.total} ${f.total === 1 ? 'Eintrag' : 'Einträge'}`];
        /* days = Fehltage laut Stundenplan (nicht Kalendertage), hours = Fehlstunden */
        if (f.days)  teile.push(`${f.days} ${f.days === 1 ? 'Fehltag' : 'Fehltage'}`);
        if (f.hours) teile.push(`${f.hours} Std.`);
        if (f.open)  teile.push(`${f.open} offen`);
        sub.textContent = teile.join(' · ');
      }
    }
    if (state.isStudent) rechteAnwenden();
    if (!state.isStudent && d.classes && d.classes.length){
      listeStand.klasse = d.classes[0].id;
      $('#clSub').textContent = d.classes.map(c => c.name).join(', ')
        + (d.openExcuses ? ` · ${d.openExcuses} offen` : '');
      $('#btnClassLead').hidden = false;
    }
  } catch (e){ /* ohne Verbindung bleibt die Startseite wie sie ist */ sptStart(); }
})();

/* Erscheinungsbild: drei Knöpfe nebeneinander, alle sichtbar.
   Vorher sprang eine Zeile bei jedem Tippen zum nächsten Wert — man musste
   dreimal drücken, um zu sehen, was es überhaupt gibt, und landete
   womöglich wieder am Anfang. */
function themeSegZeichnen(){
  const jetzt = currentTheme();
  $$('#themeSeg button').forEach(b =>
    b.classList.toggle('is-on', b.dataset.theme === jetzt));
}
$('#themeSeg').addEventListener('click', e => {
  const b = e.target.closest('button[data-theme]');
  if (!b) return;
  setTheme(b.dataset.theme);
  themeSegZeichnen();
});
themeSegZeichnen();

/* =====================================================================
   Sheets
   ===================================================================== */
const sheetTimers = new WeakMap();

function openSheet(sheet, scrim){
  clearTimeout(sheetTimers.get(sheet));      // laufendes Ausblenden abbrechen
  scrim.hidden = false; sheet.hidden = false;
  requestAnimationFrame(() => { scrim.classList.add('is-open'); sheet.classList.add('is-open'); });
}
function closeSheet(sheet, scrim){
  scrim.classList.remove('is-open'); sheet.classList.remove('is-open');
  clearTimeout(sheetTimers.get(sheet));
  sheetTimers.set(sheet, setTimeout(() => { scrim.hidden = true; sheet.hidden = true; }, 260));

  /* Nach dem Stundendetail den Plan nachladen. Wer dort eine Notiz oder
     Hausaufgaben einträgt, erwartet das Zeichen sofort auf der Kachel —
     und nicht erst beim nächsten Wochenwechsel. Der Abgleich läuft still;
     ändert sich nichts, sieht man auch nichts. */
  if (sheet && sheet.id === 'sheetDetail') planNachladen();
}

/* ---------------------------------------------------------------------
   Stiller Abgleich mit dem Server
   ---------------------------------------------------------------------
   Kein Ladebalken, kein Zurückspringen: Die Woche wird neu geholt und nur
   dann neu gezeichnet, wenn sie sich unterscheidet. Mehrere Anlässe
   führen hierher — das Schließen des Detailfensters, der Zeitgeber, die
   Rückkehr zur App —, deshalb wird zusammengefasst: Läuft schon ein
   Abgleich, wird kein zweiter gestartet.
   --------------------------------------------------------------------- */
let abgleichLaeuft = false;

async function planNachladen(){
  if (abgleichLaeuft || !navigator.onLine) return;
  abgleichLaeuft = true;
  try {
    const neu = await loadWeekQuiet(state.entity, state.weekStart);
    /* Verglichen wird der Inhalt, nicht der Zeitpunkt: Wird ohne Not neu
       gezeichnet, springt der Rollbereich und eine offene Liste schließt
       sich. loadWeek liefert `byDate` (nicht `days`) — der frühere
       Vergleich lief deshalb ins Leere, und ein neues Info-Zeichen kam
       erst nach dem nächsten Neuladen der Seite. Verglichen wird der
       festgehaltene Serverstand (`roh`), nicht die gezeichneten Karten. */
    if (neu && neu.byDate && neu.key === weekKey(state.entity, state.weekStart)
        && (neu.roh === undefined || neu.roh !== (state.week || {}).roh
            || !!neu.gesperrt !== !!(state.week || {}).gesperrt)){
      const top = $('#gridScroll').scrollTop;
      state.week = neu;
      renderGrid();
      $('#gridScroll').scrollTop = top;
      if (state.screen === 'start') renderStart();
    }
  } catch (e){ /* ohne Verbindung bleibt stehen, was da ist */ }
  finally { abgleichLaeuft = false; }
}

/* Alle zwei Minuten von selbst: Vertretungen kommen im Lauf des Tages
   dazu, und niemand zieht dafür die Seite herunter. Im Hintergrund oder
   ohne Netz wird nicht abgefragt — das kostete nur Akku. */
setInterval(() => {
  if (document.hidden) return;
  planNachladen();
}, 120000);

/* Und beim Zurückkommen zur App: Wer sie eine Stunde weggelegt hat, sieht
   sonst den Stand von vorhin. */
document.addEventListener('visibilitychange', () => {
  if (!document.hidden) planNachladen();
});
function bindSheet(sheetSel, scrimSel){
  const sheet = $(sheetSel), scrim = $(scrimSel);
  scrim.addEventListener('click', () => closeSheet(sheet, scrim));
  if (sheet.hasAttribute('data-nodrag'))
    return { sheet, scrim, open:() => openSheet(sheet, scrim), close:() => closeSheet(sheet, scrim) };
  /* Gezogen wird nur am Griff und in der Kopfzeile. Läge die Geste auf dem
     ganzen Sheet, würde jedes Scrollen in der Liste das Fenster schließen. */
  let y0 = 0, dy = 0, dragging = false;
  /* Auf breiten Geräten sind die Sheets mittige Modals — dort kein Wisch-
     zu-schließen, sonst zerreißt die Transform die Zentrierung. */
  const wide = () => window.matchMedia('(min-width:700px)').matches;
  const handle = e => !!e.target.closest('.sheet__grab, .sheet__title, .pk__top, .detail__top, .dhead, .detail__sep')
                   || e.target === sheet;

  sheet.addEventListener('touchstart', e => {
    dragging = handle(e) && !wide();
    if (!dragging) return;
    y0 = e.touches[0].clientY; dy = 0;
  }, { passive:true });

  sheet.addEventListener('touchmove', e => {
    if (!dragging) return;
    dy = Math.max(0, e.touches[0].clientY - y0);
    if (dy > 0) sheet.style.transform = `translateY(${dy}px)`;
  }, { passive:true });

  sheet.addEventListener('touchend', () => {
    if (!dragging) return;
    dragging = false;
    sheet.style.transform = '';
    if (dy > 90) closeSheet(sheet, scrim);
  }, { passive:true });
  return { sheet, scrim, open:() => openSheet(sheet, scrim), close:() => closeSheet(sheet, scrim) };
}

const SH = {
  picker: bindSheet('#sheetPicker', '#scrimPicker'),
  detail: bindSheet('#sheetDetail', '#scrimDetail'),
  menu:   bindSheet('#sheetMenu',   '#scrimMenu'),
  week:   bindSheet('#sheetWeek',   '#scrimWeek'),
  legend: bindSheet('#sheetLegend', '#scrimLegend'),
  write:  bindSheet('#sheetWrite',  '#scrimWrite'),
  to:     bindSheet('#sheetTo',     '#scrimTo'),
  info:   bindSheet('#sheetInfo',   '#scrimInfo'),
  ent:    bindSheet('#sheetEnt',    '#scrimEnt')
};
$('#infoClose').addEventListener('click', () => SH.info.close());

/* ---------- Auswahl: erst Kategorie, dann Liste ---------- */
let pickerType = null;                    // null = Kategorieebene

/* Schüler- und Elternkonten sehen keine Lehrerpläne. Der Server bietet
   sie dann gar nicht erst an; hier zur Sicherheit noch einmal, falls
   ältere Daten aus dem Speicher kommen. */
function keineLehrerplaene(){
  const r = (state.user && state.user.rolle) || '';
  return !!state.isStudent || r === 'schueler' || r === 'eltern';
}

function pickerShowRoot(){
  pickerType = null;
  if (keineLehrerplaene()) state.entities = state.entities.filter(e => e.type !== 'teacher');
  /* Gibt es zu einer Art gar keine Einträge — bei Schüler*innen etwa keine
     Lehrkräfte —, verschwindet auch der Knopf dafür. */
  $$('#pickerRoot [data-open]').forEach(b => {
    b.hidden = !state.entities.some(e => e.type === b.dataset.open);
  });
  /* Erst leeren, dann das Kreuz im Feld danach richten — sonst blieb es
     nach einer Suche beim nächsten Öffnen im leeren Feld stehen. */
  $('#pickerSearch').value = '';
  const wipe = $('#pickerWipe');
  if (wipe) wipe.hidden = true;
  $('#pickerRoot').hidden = false;
  $('#pickerList').hidden = true;
  $('#pickerIndex').hidden = true;
  $('#pickerBody').scrollTop = 0;
  $('#pickerTitle').textContent = 'Stundenplan auswählen';
  $('#pickerSearch').value = '';
}

const PK_TITLE = { class:'Klassen', teacher:'Lehrkräfte', room:'Räume', mine:'Mein Stundenplan' };

function pickerOpen(type){
  pickerType = type;
  $('#pickerRoot').hidden = true;
  $('#pickerList').hidden = false;
  $('#pickerTitle').textContent = PK_TITLE[type] || 'Auswahl';
  renderPicker();
}

/* REGISTER-BEGINN – Alphabet-Register der Planauswahl (in sich
   geschlossen; die Tests führen diesen Abschnitt mit Node aus). */
const PK_ALPHABET = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ#'.split('');
/* Buchstabe fürs Register: Umlaute zum Grundbuchstaben, Ziffern und
   Zeichen unter „#“. */
function pkBuchstabe(label){
  const z = String(label || '').trim().charAt(0).toUpperCase()
    .normalize('NFD').replace(/[̀-ͯ]/g, '');
  return /[A-Z]/.test(z) ? z : '#';
}
/* Ziel eines Registerbuchstabens: der Buchstabe selbst oder der nächste
   vorhandene danach (am Ende der letzte davor). */
function pkZiel(l, vorhanden){
  if (!vorhanden || !vorhanden.length) return null;
  const i = PK_ALPHABET.indexOf(l);
  if (i < 0) return vorhanden[0];
  for (let k = i; k < PK_ALPHABET.length; k++) if (vorhanden.includes(PK_ALPHABET[k])) return PK_ALPHABET[k];
  for (let k = i - 1; k >= 0; k--) if (vorhanden.includes(PK_ALPHABET[k])) return PK_ALPHABET[k];
  return vorhanden[0];
}
window.UntPlanRegister = { alphabet: PK_ALPHABET, buchstabe: pkBuchstabe, ziel: pkZiel };
/* REGISTER-ENDE */

let pkLetzter = null;
function pkSpringen(l, vorhanden){
  const ziel = pkZiel(l, vorhanden);
  if (!ziel || ziel === pkLetzter) return;
  pkLetzter = ziel;
  const t = document.getElementById('pk-l-' + ziel);
  const body = $('#pickerBody');
  if (!t || !body) return;
  /* Der Buchstabenkopf klebt oben (sticky) und meldet dann nicht seine
     eigentliche Lage — gemessen wird deshalb an der ersten Zeile darunter. */
  const kopf = t.classList.contains('picker-letter');
  const zeile = kopf ? (t.nextElementSibling || t) : t;
  const delta = zeile.getBoundingClientRect().top - body.getBoundingClientRect().top - (kopf ? t.offsetHeight : 0);
  body.scrollTop = Math.max(0, body.scrollTop + delta - 2);
  if (navigator.vibrate) try { navigator.vibrate(4); } catch (e) {}
}

function pkRegister(vorhanden, zeigen){
  const idx = $('#pickerIndex');
  if (!zeigen){ idx.hidden = true; idx.innerHTML = ''; return; }
  const zeichen = PK_ALPHABET.filter(l => l !== '#' || vorhanden.includes('#'));
  idx.hidden = false;
  idx.setAttribute('role', 'navigation');
  idx.setAttribute('aria-label', 'Alphabet-Register');
  idx.innerHTML = zeichen.map(l =>
    `<button type="button" data-l="${l}" class="${vorhanden.includes(l) ? '' : 'is-leer'}" aria-label="Zu ${l === '#' ? 'Ziffern' : l}">${l}</button>`).join('');
  idx._vorhanden = vorhanden;
}

/* Tippen und Ziehen über das Register — einmal gebunden, liest die
   aktuell vorhandenen Buchstaben jeweils neu. */
(function pkRegisterBinden(){
  const idx = $('#pickerIndex');
  if (!idx) return;
  const unter = (x, y) => {
    const el = document.elementFromPoint(x, y);
    const b = el && el.closest ? el.closest('#pickerIndex button') : null;
    if (b) return b.dataset.l;
    /* Neben die Buchstaben gezogen: nach der Höhe zuordnen. */
    const btns = $$('#pickerIndex button');
    if (!btns.length) return null;
    const r0 = btns[0].getBoundingClientRect(), r1 = btns[btns.length - 1].getBoundingClientRect();
    if (y <= r0.top) return btns[0].dataset.l;
    if (y >= r1.bottom) return btns[btns.length - 1].dataset.l;
    return null;
  };
  let aktiv = false;
  const los = (x, y) => {
    const l = unter(x, y);
    if (!l) return;
    idx.classList.add('is-ziehen');
    $$('#pickerIndex button').forEach(b => b.classList.toggle('is-on', b.dataset.l === l));
    pkSpringen(l, idx._vorhanden || []);
  };
  const ende = () => {
    aktiv = false; pkLetzter = null;
    idx.classList.remove('is-ziehen');
    $$('#pickerIndex button.is-on').forEach(b => b.classList.remove('is-on'));
  };
  idx.addEventListener('pointerdown', e => {
    aktiv = true; pkLetzter = null;
    try { idx.setPointerCapture(e.pointerId); } catch (x) {}
    e.preventDefault();
    los(e.clientX, e.clientY);
  });
  idx.addEventListener('pointermove', e => { if (aktiv){ e.preventDefault(); los(e.clientX, e.clientY); } });
  idx.addEventListener('pointerup', ende);
  idx.addEventListener('pointercancel', ende);
  /* Ziehen darf das Blatt nicht verschieben. */
  idx.addEventListener('touchstart', e => e.stopPropagation(), { passive:true });
  idx.addEventListener('touchmove', e => { e.preventDefault(); e.stopPropagation(); }, { passive:false });
})();

function renderPicker(){
  const q = $('#pickerSearch').value.trim().toLowerCase();

  /* Ohne gewählte Kategorie sucht das Feld über alle Pläne hinweg. */
  let list = state.entities.filter(e => pickerType ? e.type === pickerType : true);
  if (q) list = list.filter(e =>
    e.label.toLowerCase().includes(q) || (e.info || '').toLowerCase().includes(q));

  /* Für die Buchstabengruppen muss die Liste sortiert sein — auf die
     Reihenfolge aus der Datenbank ist kein Verlass. */
  list = list.slice().sort((a, b) =>
    a.label.localeCompare(b.label, 'de', { numeric:true, sensitivity:'base' }));

  if (!pickerType && !q){ pickerShowRoot(); return; }
  if (!pickerType){
    $('#pickerRoot').hidden = true;
    $('#pickerList').hidden = false;
    $('#pickerTitle').textContent = 'Suchergebnisse';
  }

  const idx = $('#pickerIndex');
  if (!list.length){
    $('#pickerList').innerHTML = '<div class="empty" style="margin:10px 12px">Keine Treffer.</div>';
    idx.hidden = true;
    return;
  }

  const CHECK = '<svg viewBox="0 0 24 24"><path d="m5 12.5 4.5 4.5L19 7"/></svg>';
  const TYPE_CHIP = { class:'Klasse', teacher:'Lehrkraft', room:'Raum', student:'Schüler*in', mine:'Mein Stundenplan' };
  /* Zeichen je Planart — dieselben wie in der Übersicht darüber. */
  const PICK_SYM = { class:'ic-shared-classentity', teacher:'ic-shared-teacherentity', room:'ic-shared-roomentity',
                     student:'ic-lessons-students', mine:'ic-my-timetable' };
  const grouped = !q && pickerType && pickerType !== 'mine';

  let html = q ? `<div class="pk__count">${list.length} ${list.length === 1 ? 'Treffer' : 'Treffer'}</div>` : '';
  let letter = null;
  const letters = [];
  list.forEach(e => {
    const l = pkBuchstabe(e.label);
    /* Erste Zeile je Buchstabe trägt den Anker für das Register — auch in
       den Suchergebnissen, die keine Buchstabenköpfe zeigen. */
    let anker = '';
    if (l !== letter){
      letter = l; letters.push(l);
      if (grouped) html += `<div class="picker-letter" id="pk-l-${esc(letter)}">${esc(letter)}</div>`;
      else anker = ` id="pk-l-${esc(letter)}"`;
    }
    const active = e === state.entity;
    html += `<button class="picker-item ${active ? 'is-active' : ''}"${anker}
      data-type="${e.type}" data-id="${e.id}">
      <span class="picker-item__ava picker-item__ava--sym">${PICK_SYM[e.type] ? `<svg class="uic" viewBox="0 0 28 28" aria-hidden="true"><use href="#${PICK_SYM[e.type]}"></use></svg>` : esc((e.label || '?').slice(0, 3).toUpperCase())}</span>
      <span class="picker-item__main"><b>${esc(e.label)}</b>
        ${TYPE_CHIP[e.type] ? `<span class="picker-item__chip">${esc(TYPE_CHIP[e.type])}</span>` : ''}
        ${e.classTeacher ? `<span class="picker-item__chip picker-item__chip--kl">Klassenlehrkraft: ${esc(e.classTeacher)}</span>` : ''}</span>
      <span class="picker-item__mark">${active ? CHECK : ''}</span>
    </button>`;
  });
  $('#pickerList').innerHTML = html;

  /* Alphabet-Register A–Z am rechten Rand (wie in der iOS-App): immer alle
     Buchstaben; fehlt einer, springt das Register zum nächsten vorhandenen.
     Tippen und Ziehen springen. */
  pkRegister(letters, pickerType !== 'mine' && list.length > 1);

  $$('#pickerList .picker-item').forEach(b => b.addEventListener('click', () => {
    const neu = state.entities.find(e => e.type === b.dataset.type && String(e.id) === b.dataset.id);
    if (!neu) return;
    SH.picker.close();
    planWechseln(neu);
  }));
}

/* iOS schiebt beim Fokus in ein Eingabefeld gern das ganze Fenster hoch.
   Da alles fest positioniert ist, bliebe dann weiße Fläche stehen — also
   wird die Ansicht danach wieder an ihren Platz gezogen. */
function pinViewport(){
  window.scrollTo(0, 0);
  if (document.scrollingElement) document.scrollingElement.scrollTop = 0;
  document.body.scrollTop = 0;
}
if (window.visualViewport)
  window.visualViewport.addEventListener('scroll', () => {
    if (!$('#sheetPicker').hidden || !$('#sheetDetail').hidden) pinViewport();
  });

['#pickerSearch', '#loginUser', '#loginPass'].forEach(sel => {
  const el = $(sel);
  if (!el) return;
  el.addEventListener('focus', () => {
    pinViewport();
    setTimeout(pinViewport, 60);
    setTimeout(pinViewport, 300);
  });
});
document.addEventListener('focusin', e => {
  if (e.target.closest('#detailSub')) { pinViewport(); setTimeout(pinViewport, 120); }
});

/* Tipp auf das Plan-Kürzel oben öffnet direkt die Planauswahl. Das
   Suchfeld steht oben, bekommt aber keinen Fokus — sonst schöbe iOS sofort
   die Tastatur über die Liste. Gefiltert wird live beim Tippen. */
function openPicker(){
  pickerShowRoot();
  SH.picker.open();
  const i = $('#pickerSearch');
  if (i && document.activeElement === i) i.blur();
}
$('#btnEntityPicker').addEventListener('click', openPicker);
/* =====================================================================
   Profilwechsel
   ---------------------------------------------------------------------
   Mehrere Konten liegen auf dem Gerät bereit; der Wechsel geht ohne
   erneute Anmeldung. Die Liste kommt von api/accounts.
   ===================================================================== */
const ACC_URL = new URL('api/accounts', location.href).href;

async function profilListe(){
  const box = $('#profList');
  box.innerHTML = '<p class="profpop__hint">Wird geladen …</p>';
  let d;
  try {
    d = await fetch(ACC_URL + '?action=list', { credentials:'same-origin' }).then(r => r.json());
  } catch (e){
    box.innerHTML = '<p class="profpop__hint">Nicht erreichbar</p>';
    return;
  }
  const list = (d && d.accounts) || [];
  if (list.length < 2){
    box.innerHTML = '<p class="profpop__hint">Bisher ist nur dieses Konto angemeldet.</p>'
      + '<button class="profrow profrow--add" id="profAdd"><span class="profrow__mark"></span><span class="profrow__main"><b>Weiteres Profil anmelden</b></span></button>';
    $('#profAdd').addEventListener('click', e => { e.stopPropagation(); profilHinzufuegen(); });
    return;
  }
  box.innerHTML = list.map(a => `
    <button class="profrow ${a.current ? 'is-on' : ''}" data-token="${esc(a.token)}">
      <span class="profrow__mark">${a.current
        ? '<svg viewBox="0 0 24 24"><path d="m5 12.5 4.5 4.5L19 7"/></svg>' : ''}</span>
      <span class="profrow__main">
        <b>${esc(a.label || 'Profil')}</b>
        ${a.info ? `<span>${esc(a.info)}</span>` : ''}
      </span>
      <span class="profrow__del" data-del="${esc(a.token)}" role="button" aria-label="Profil entfernen">
        <svg viewBox="0 0 24 24"><path d="m6 6 12 12M18 6 6 18"/></svg>
      </span>
    </button>`).join('');

  $$('#profList .profrow').forEach(b => b.addEventListener('click', async e => {
    if (e.target.closest('[data-del]')){
      e.stopPropagation();
      const t = e.target.closest('[data-del]').dataset.del;
      const aktuell = b.classList.contains('is-on');
      if (aktuell && !window.confirm('Dieses Profil auf dem Gerät abmelden?')) return;
      const r = await fetch(ACC_URL, { method:'POST', credentials:'same-origin',
        headers:{ 'Content-Type':'application/x-www-form-urlencoded' },
        body:new URLSearchParams({ action:'remove', token:t }) }).then(x => x.json()).catch(() => null);
      /* Das laufende Profil entfernt: ein anderes übernimmt oder es geht
         zur Anmeldung — in beiden Fällen sauber neu aufbauen. */
      if (r && (r.switched || r.loggedOut)){
        try { localStorage.clear(); } catch (e2){}
        if (r.loggedOut) try { sessionStorage.setItem('sso:versucht', '1'); } catch (e2){}
        location.reload();
        return;
      }
      profilListe();
      return;
    }
    if (b.classList.contains('is-on')){ $('#profPop').hidden = true; return; }
    try {
      const r = await fetch(ACC_URL, { method:'POST', credentials:'same-origin',
        headers:{ 'Content-Type':'application/x-www-form-urlencoded' },
        body:new URLSearchParams({ action:'switch', token:b.dataset.token })
      }).then(x => x.json());
      if (!r.ok) throw new Error(r.error || 'Fehler');
      $('#profPop').hidden = true;
      buzz();
      /* Gemerkte Wochen und Einstellungen des anderen Kontos verwerfen,
         das Erscheinungsbild bleibt. */
      try { const th = localStorage.getItem('tt.theme'); localStorage.clear(); if (th) localStorage.setItem('tt.theme', th); } catch (e2){}
      /* Nach dem Wechsel gehört alles einem anderen Konto — sauber neu
         aufbauen, statt Reste des alten stehen zu lassen. */
      location.reload();
    } catch (e){ toast('Wechsel nicht möglich: ' + e.message); }
  }));
}

$('#btnSwitchEntity').addEventListener('click', e => {
  e.stopPropagation();
  const pop = $('#profPop');
  if (!pop.hidden){ pop.hidden = true; return; }
  pop.hidden = false;
  profilListe();
});
/* Die Kontokarte auf der Profilseite führt zum selben Fenster wie der
   Knopf oben. Sie trägt den Doppelpfeil und sieht damit aus, als ließe
   sich hier wechseln — bisher tat sie beim Antippen nichts. */
$('#btnSwitchAccount').addEventListener('click', () => {
  const pop = $('#profPop');
  pop.hidden = false;
  profilListe();
});

/* Ein weiteres Konto wird auf der Profilseite hinzugefügt — nicht im
   Wechselfenster. Dort geht es ums Wechseln, nicht ums Einrichten. */
function profilHinzufuegen(){
  $('#profPop').hidden = true;
  /* Anmeldemaske zeigen, ohne das laufende Konto abzumelden — das neue
     wird danach zusätzlich hinterlegt (serverseitig je Gerät). Mit
     „Abbrechen“ geht es zurück zum bisherigen Konto. */
  const ov = $('#login');
  if (ov){
    ov.hidden = false;
    ov.classList.add('is-add');
    $('#loginCancel').hidden = false;
    const u = $('#loginUser'); if (u){ u.value = ''; setTimeout(() => u.focus(), 120); }
    const pw = $('#loginPass'); if (pw) pw.value = '';
    const er = $('#loginError'); if (er) er.hidden = true;
  } else toast('Anmeldung nicht verfügbar');
}
$('#btnAddProfile').addEventListener('click', profilHinzufuegen);
$('#loginCancel').addEventListener('click', () => {
  const ov = $('#login');
  ov.hidden = true; ov.classList.remove('is-add');
  $('#loginCancel').hidden = true;
});
document.addEventListener('click', e => {
  const pop = $('#profPop');
  if (pop && !pop.hidden && !e.target.closest('#profPop') && !e.target.closest('#btnSwitchEntity'))
    pop.hidden = true;
});

/* Eingabetaste wählt den ersten Treffer. */
$('#pickerSearch').addEventListener('keydown', e => {
  if (e.key !== 'Enter') return;
  const first = $('#pickerList .picker-item');
  if (first){ e.preventDefault(); first.click(); }
});

$('#pickerBack').addEventListener('click', () => {
  if (pickerType || $('#pickerList').hidden === false) pickerShowRoot();
  else SH.picker.close();
});
/* Rundes Kreuz im Feld: nur die Eingabe löschen.
   Rundes Kreuz daneben: Auswahl schließen. */
$('#pickerWipe').addEventListener('click', () => {
  const inp = $('#pickerSearch');
  inp.value = '';
  $('#pickerWipe').hidden = true;
  inp.focus();
  pickerType ? renderPicker() : pickerShowRoot();
});
$('#pickerClear').addEventListener('click', () => SH.picker.close());
$$('#pickerRoot .pk__row').forEach(b => b.addEventListener('click', () => pickerOpen(b.dataset.open)));
$('#pickerRoot .pk__mine').addEventListener('click', () => {
  const own = state.entities.find(e => e.type === 'mine') || state.entities[0];
  SH.picker.close();
  planWechseln(own);
});
$('#pickerSearch').addEventListener('input', () => {
  $('#pickerWipe').hidden = $('#pickerSearch').value.trim() === '';
  renderPicker();
});

/* Info zum angezeigten Plan: eigenes Blatt wie die Stundendetails. */
$('#btnInfo').addEventListener('click', () => {
  $('#infoTitle').textContent = state.entity.label || 'Information';
  const zeilen = String(state.entity.info || '').split(/\n|\s+·\s+/).map(x => x.trim()).filter(Boolean);
  $('#infoBody').innerHTML = zeilen.length
    ? `<div class="info__group">${zeilen.map(z => `<p class="info__row">${esc(z)}</p>`).join('')}</div>`
    : '<p class="info__leer">Keine weiteren Angaben.</p>';
  SH.info.open();
});

/* ---------- Menü ---------- */
/* Der Haken sitzt bei der gerade laufenden Ansicht — wie im Muster. Vor
   jedem Öffnen frisch gesetzt, damit er dem tatsächlichen Stand folgt. */
function markMenuView(){
  $$('#sheetMenu .menu-item--view').forEach(b => {
    const on = b.dataset.act === 'view-' + state.view;
    b.classList.toggle('is-active', on);
    b.setAttribute('aria-checked', on ? 'true' : 'false');
  });
  /* „Abwesenheiten anzeigen“ gilt nur für den eigenen Plan. */
  const abw = $('#menuAbsences');
  if (abw){
    const eigen = !!state.entity && state.entity.type === 'mine';
    abw.hidden = !eigen;
    const sep = $('#menuAbsencesSep'); if (sep) sep.hidden = !eigen;
    abw.classList.toggle('is-active', !!state.opts.showAbsences);
    abw.setAttribute('aria-checked', state.opts.showAbsences ? 'true' : 'false');
  }
}
/* ---------- Aktualisieren ----------
   Holt die angezeigte Woche noch einmal vom Server. Vorher wird alles
   verworfen, was sonst dazwischenkäme und genau den Stand zurückgäbe, der
   ohnehin schon dasteht: die mitgelieferte Startwoche, die vorgeladenen
   Nachbarwochen und eine gerade laufende Anfrage derselben Woche.

   Die gespeicherte Kopie bleibt liegen — sie ist der Rückfall ohne Netz.
   Der Service Worker holt Daten ohnehin zuerst aus dem Netz, sie kann
   also nicht in die Quere kommen. Nebenbei fragt die App nach, ob eine
   neue Fassung der App selbst bereitliegt. */
async function datenNeuLaden(){
  bootUsed = true;
  state.around = {}; state.aroundKey = null;
  laufendeLadungen.clear();
  wochenVergessen();

  try {
    const reg = navigator.serviceWorker && await navigator.serviceWorker.getRegistration();
    if (reg) reg.update();
  } catch (e){}

  await refresh();
  toast('Plan aktualisiert');
}

$('#btnMenu').addEventListener('click', () => { markMenuView(); SH.menu.open(); });
$$('#sheetMenu .menu-item').forEach(b => b.addEventListener('click', () => {
  const act = b.dataset.act;
  SH.menu.close();
  setTimeout(() => {
    if (act === 'reload'){ datenNeuLaden(); }
    if (act === 'pickweek'){
      /* Vorgewählt ist der Tag, der gerade vorne steht. */
      state.calSel = tageAnsicht() ? state.day : (weekDates(state.weekStart).includes(iso(new Date())) ? iso(new Date()) : state.weekStart);
      state.calMonth = parseISO(state.calSel);
      renderCalendar(); SH.week.open();
    }
    if (act && act.startsWith('view-')){
      const v = act.slice(5);
      if (state.view !== v){
        /* Von der Woche in eine Tagesansicht: heute, wenn heute in der
           gezeigten Woche liegt, sonst der Montag. */
        if (TAGE_ANSICHT[v] && !tageAnsicht()) state.day = pickDefaultDay();
        setView(v);
      }
    }
    if (act === 'legend'){ SH.legend.open(); }
    if (act === 'toggle-absences'){
      state.opts.showAbsences = !state.opts.showAbsences;
      store.set('showAbsences', state.opts.showAbsences);
      renderGrid();
    }
  }, 200);
}));

/* ---------- Gehe zu Datum ----------
   Popover wie im Original: oben „Heute“ und „Auswählen“, darunter der
   Monat mit Pfeilen und die Tage MO–SO. Heute steht farbig, der gewählte
   Tag mit Kreis. „Auswählen“ springt zum gewählten Tag – in der Woche zu
   dessen Woche, in den Tagesansichten auf genau diesen Tag. */
function datumAnspringen(isoDay){
  const tag = schultag(isoDay);
  state.weekStart = iso(mondayOf(parseISO(tag)));
  state.around = {};
  if (tageAnsicht()) state.day = tag;
  SH.week.close();
  refresh();
}

function renderCalendar(){
  const m = state.calMonth;
  $('#calMonth').textContent = `${MONTHS[m.getMonth()]} ${m.getFullYear()}`;
  const erster = new Date(m.getFullYear(), m.getMonth(), 1);
  const leer = (erster.getDay() + 6) % 7;
  const tage = new Date(m.getFullYear(), m.getMonth() + 1, 0).getDate();
  const heute = iso(new Date());
  let html = '';
  for (let i = 0; i < leer; i++) html += '<span class="dpday is-leer"></span>';
  for (let t = 1; t <= tage; t++){
    const d = new Date(m.getFullYear(), m.getMonth(), t), di = iso(d);
    const cls = ['dpday'];
    if (di === heute) cls.push('is-today');
    if (di === state.calSel) cls.push('is-sel');
    if (d.getDay() === 0 || d.getDay() === 6) cls.push('is-weekend');
    html += `<button type="button" class="${cls.join(' ')}" data-date="${di}"><span>${t}</span></button>`;
  }
  $('#calGrid').innerHTML = html;
  $$('#calGrid .dpday[data-date]').forEach(b => b.addEventListener('click', () => {
    state.calSel = b.dataset.date;
    renderCalendar();
  }));
}
$('#calPrev').addEventListener('click', () => { state.calMonth = new Date(state.calMonth.getFullYear(), state.calMonth.getMonth()-1, 1); renderCalendar(); });
$('#calNext').addEventListener('click', () => { state.calMonth = new Date(state.calMonth.getFullYear(), state.calMonth.getMonth()+1, 1); renderCalendar(); });
$('#calToday').addEventListener('click', () => datumAnspringen(iso(new Date())));
$('#calPick').addEventListener('click', () => datumAnspringen(state.calSel || iso(new Date())));

/* =====================================================================
   Stundendetails
   ===================================================================== */
function flatCards(){
  const out = [];
  /* Auch die Vorschau der nächsten Tage folgt dem eigenen Plan — und
     weicht nicht auf einen fremden aus, wenn der eigene fehlt. */
  const eig = state.entities.find(e => e.type === 'mine') || state.entities[0];
  const q = (startWeek && startWeek.byDate)
         || (planKey(state.entity) === planKey(eig) ? state.week.byDate : {});
  weekDates(state.weekStart).forEach(d => (q[d] || []).forEach(c => out.push(c)));
  return out.sort((a,b) => a.date.localeCompare(b.date) || a.lesson - b.lesson);
}

function openDetail(date, lesson, uid, quelle){
  /* Von der Startseite kommen Karten aus dem eigenen Plan, aus dem Raster
     aus dem gerade angezeigten. Wird die falsche Sammlung durchsucht,
     öffnet sich die Stunde eines anderen Plans. */
  const pool = quelle || state.allByDate || state.week.byDate;
  const all  = pool[date] || [];
  const card = all.find(c => c._uid === uid) || all.find(c => c.lesson === lesson);
  if (!card) return;
  state.detailFull = card;          /* der ganze Block */
  state.detail = card;              /* voreingestellt gilt der ganze Block */
  $('#detailPart').hidden = true;
  $('#detailMenuPop').hidden = true;
  $('#detailAbs').hidden = true;
  $('#detailAbsList').hidden = true;
  $('#dabsPick').hidden = true;
  $('#detailForm').hidden = true;
  $('#detailSub').hidden = true;
  cbCache = {};
  /* Ab hier zählt die einzelne Stunde, nicht der Block — sonst lädt die
     App die Inhalte des Blocks und zeigt sie an einer Stunde an. */
  const teil = state.detail;
  attReset(teil, true);
  renderDetail();
  SH.detail.open();
  cbLoadAll(teil).then(() => { if (state.detail === teil) renderDetail(); });
}

function longName(kind, code){
  if (!state.opts.longNames || !code) return code;
  const c = String(code).toUpperCase();
  /* Die Langnamen kommen aus den Stammdaten von UntPlanWeb. */
  const N = (BOOT && BOOT.names) || {};
  if (kind === 'subject') return (N.subject || {})[c] || S.SUBJECT_NAMES[c] || code;
  if (kind === 'teacher') return (N.teacher || {})[c] || S.TEACHER_NAMES[c] || code;
  if (kind === 'room')    return (N.room || {})[code] || S.ROOM_NAMES[code] || S.ROOM_NAMES[c] || code;
  return code;
}

/* Ohne Recht kein Knopf: Was Schüler*innen nicht dürfen, sollen sie auch
   nicht angeboten bekommen. Ausgeblendet wird, nicht nur gesperrt. */
function rechteAnwenden(){
  const weg = [
    '#btnClassLead',      /* Klassenleitung auf der Startseite   */
    '#dsubAddTop',        /* Neuer Eintrag in den Unterlisten    */
    '#dsubSave',          /* Speichern und Kontrolle bestätigen  */
    '#dabsSave', '#dabsDel', '#dabsClose',   /* Abwesenheitsfenster */
    '#dformSave',         /* Eingabefenster sichern              */
    '#attSeg',            /* Filter der Schülerliste             */
    '#btnPrint'           /* Ausdruck des Klassenbuchs           */
  ];
  /* Das Menü der Stunde bleibt stehen. Es enthält nichts zum Bearbeiten,
     sondern führt allein in andere Pläne — zur Klasse, mit der man
     gemeinsam unterrichtet wird, oder in den Raum. Genau das ist auch für
     Schüler*innen sinnvoll; Lehrerpläne tauchen dort ohnehin nicht auf,
     weil sie in der Auswahlliste fehlen und der Eintrag damit wegfällt.
     Was zum Bearbeiten gehört, wird weiterhin ausgeblendet. */
  ['#dsubAddTop', '#dsubSave',
   '#dabsSave', '#dabsDel', '#dformSave'].forEach(sel => {
    const el = $(sel); if (el){ el.hidden = true; el.style.display = 'none'; }
  });
  weg.forEach(sel => {
    const el = $(sel);
    if (!el) return;
    el.hidden = true;
    el.style.display = 'none';     /* auch gegen späteres Einblenden */
  });
  /* Zeilen im Detail, die zu einer Unteransicht führen, die es für
     Schüler*innen nicht gibt. */
  $$('#detailBody .drow2').forEach(z => {
    /* Diese beiden bleiben offen: Der Vertretungstext ist ohnehin nur zu
       lesen, und hinter den Hausaufgaben steht eine Liste, die in eine
       Zeile nicht passt. */
    if (['substitution', 'info', 'exam', 'join', 'homework', 'lehrstoff', 'notes_students'].includes(z.dataset.key)) return;
    if (['students', 'classbook', 'notes_teachers', 'online', 'onlinelink'].includes(z.dataset.key)){
      z.remove();
      return;
    }
    /* Was bleibt, ist nur zu lesen: kein Pfeil, kein Antippen. */
    z.classList.add('is-readonly');
    z.disabled = true;
    const pf = z.querySelector('.drow2__end');
    if (pf) pf.innerHTML = '';
  });
}

/* Schüler*innen sehen im Detail nur, was für sie bestimmt ist: Notizen
   für Schüler*innen, den Lehrstoff und die Hausaufgaben — und das jeweils
   nur, wenn wirklich etwas hinterlegt ist. Kein Klassenbuch, keine
   Anwesenheitsliste, keine Notizen für Lehrkräfte. */
function detailFuerSchueler(rows){
  return rows.reduce((out, r) => {
    /* Hausaufgaben stehen immer da — auch leer, damit man sieht, dass
       keine aufgegeben wurden. */
    if (r.key === 'homework'){ out.push(r); return out; }
    /* Der Vertretungstext gehört ebenfalls zur Stunde und geht Schüler*innen
       an — er steht ohnehin schon auf der Kachel. */
    if (r.key === 'substitution' || r.key === 'info'){
      if (r.sub && String(r.sub).trim()) out.push(r);
      return out;
    }
    /* Prüfung und der Link zum Online-Unterricht gelten für alle. */
    if (r.key === 'exam' || r.key === 'join' || r.key === 'moved'){ out.push(r); return out; }
    /* Lehrstoff und Notizen für Schüler*innen nur, wenn etwas hinterlegt
       ist — und ohne Häkchen, denn abhaken darf hier niemand. */
    if (r.key === 'lehrstoff' || r.key === 'notes_students'){
      if (r.sub && String(r.sub).trim())
        out.push(Object.assign({}, r, { done:undefined, toggle:undefined }));
      return out;
    }
    /* Alles Übrige — Schülerliste, Klassenbuch, Notizen für Lehrkräfte,
       „Findet online statt" — geht Schüler*innen nichts an. */
    return out;
  }, []);
}

/* Beschriftung eines Reiters: das Kürzeste, was die Stunde erkennbar macht. */
function detailTabLabel(c){
  if (c.blockType === 'holiday' || c.blockType === 'event') return c.subject || 'Termin';
  if (c.blockType === 'gs' || c.blockType === 'absence')    return 'Veranstaltung';
  if (c.blockType === 'standby')                            return c.subject || 'ZBer';
  if (c.blockType === 'supervision')                        return c.subject || 'Aufsicht';
  return (c.subject || c.subjectOrig || c.class || c.teacher || '·').toString();
}

function renderDetailTabs(c){
  const box = $('#detailTabs');
  if (!box) return;

  const alle = (state.allByDate || (state.week && state.week.byDate) || {})[c.date] || [];
  /* Gleichzeitig heißt: die Zeiträume überschneiden sich.

     Aufsichten bleiben außen vor. Sie liegen in einer Pause und tragen als
     Stundennummer nur den Platz davor — dadurch überschnitten sie sich
     rechnerisch mit der folgenden Stunde und tauchten als Reiter in deren
     Detailfenster auf, obwohl sie damit nichts zu tun haben. */
  const istAufsicht = x => !!(x._supervision || x._pause || x.blockType === 'supervision');
  const gleich = alle.filter(x =>
    istAufsicht(x) === istAufsicht(c)
    && x.lesson <= c.lesson + c.span - 1 && c.lesson <= x.lesson + x.span - 1);

  if (gleich.length < 2){ box.hidden = true; box.innerHTML = ''; return; }
  /* Wie in den Sollbildern: die Veranstaltung vorn, danach die Stunden. */
  const istV = x => x.blockType === 'absence' || x.blockType === 'gs';
  gleich.sort((a, b) => (istV(b) - istV(a)) || (a.lesson - b.lesson));

  box.hidden = false;
  box.innerHTML = gleich.map((x, i) =>
    `<button class="dtabs__t${x === c ? ' is-on' : ''}" type="button" role="tab"
             aria-selected="${x === c}" data-tab="${i}">${esc(detailTabLabel(x))}</button>`
  ).join('');
  box._cards = gleich;

  /* Der aktive Reiter soll sichtbar sein, auch wenn die Leiste rollt. */
  /* Nur die Leiste selbst waagerecht rollen – scrollIntoView rollte auch das
     Fenster senkrecht mit, dann rutschte der Titel unter die Reiter. */
  const on = box.querySelector('.is-on');
  if (on){
    const l = on.offsetLeft, r = l + on.offsetWidth;
    if (l < box.scrollLeft) box.scrollLeft = l;
    else if (r > box.scrollLeft + box.clientWidth) box.scrollLeft = r - box.clientWidth;
  }
}

/* Lange Werteliste im Kopf der Stundendetails kürzen: Mehr als
   KURZ_GRENZE Werte (z. B. alle Lehrkräfte oder alle Klassen einer
   Veranstaltung) zeigen nur die ersten, dahinter „… +N“. Antippen klappt
   die ganze Liste umbrechend auf (und wieder zu). `teile` sind fertiges
   HTML. */
const KURZ_GRENZE = 6;
function kurzListe(teile, sep){
  if (teile.length <= KURZ_GRENZE) return teile.join(sep);
  const zahl = KURZ_GRENZE - 1;
  const rest = teile.length - zahl;
  return `${teile.slice(0, zahl).join(sep)}<span class="dmehr__rest" hidden>${sep}${teile.slice(zahl).join(sep)}</span>`
       + `<button type="button" class="dmehr" aria-expanded="false" data-mehr="${rest}"`
       + ` aria-label="${rest} weitere anzeigen">… +${rest}</button>`;
}

function kurzListenBinden(root){
  root.querySelectorAll('.dmehr').forEach(b => b.addEventListener('click', e => {
    e.stopPropagation();
    const zeile = b.parentElement;
    const rest = zeile && zeile.querySelector('.dmehr__rest');
    if (!rest) return;
    const offen = rest.hidden;
    rest.hidden = !offen;
    zeile.classList.toggle('is-offen', offen);
    b.setAttribute('aria-expanded', String(offen));
    b.textContent = offen ? 'weniger' : `… +${b.dataset.mehr}`;
    b.setAttribute('aria-label', offen ? 'Liste einklappen' : `${b.dataset.mehr} weitere anzeigen`);
  }));
}

function renderDetail(){
  const c = state.detail;
  const d = parseISO(c.date);
  const t1 = (state.times[c.lesson] || {}).start || '';
  const t2 = (state.times[Math.min(c.lesson + c.span - 1, 12)] || {}).end || '';
  const kind = viewKind(state.entity);

  const two = n => String(n).padStart(2, '0');
  /* „Fr. | 16.10.26“ – der Strich etwas heller, wie im Sollbild. */
  $('#detailDate').innerHTML =
    `${WD_SHORT[(d.getDay()+6)%7]}<span class="detail__when-sep">|</span>${two(d.getDate())}.${two(d.getMonth()+1)}.${String(d.getFullYear()).slice(2)}`;
  $('#detailTime').textContent = `${t1.replace(/^0/, '')} - ${t2.replace(/^0/, '')}`;

  /* Bei einem Block lässt sich oben auf eine einzelne Stunde umstellen —
     Eintragungen gehören dann nur zu dieser Stunde. */
  const ganz = state.detailFull || c;
  /* Nur bei Unterricht mit Klassenbuch — Termine und Aufsichten haben keins. */
  const teilbar = detailTeilbar(ganz);
  $('#detailWhen').classList.toggle('is-pick', teilbar);
  $('#detailWhen').setAttribute('aria-haspopup', teilbar ? 'listbox' : 'false');
  $('#detailWhen').disabled = !teilbar;

  /* --- Umschalter zwischen gleichzeitigen Stunden ----------------------
     Liegen mehrere Karten zur selben Zeit — eine Kopplung, zwei Kurse
     nebeneinander, eine Veranstaltung neben dem Unterricht —, führt eine
     Leiste über dem Inhalt zu jeder von ihnen. Vorher sah man nur die,
     die man zufällig getroffen hatte. */
  renderDetailTabs(c);
  renderDetailTeil();

  /* Kopfblock nach den Mobile-Sollbildern:
       Klasse | Kursbezeichnung
       Fach  [ZUSÄTZLICHE UNTERRICHTSSTUNDE]
       alter Raum (durchgestrichen) → neuer Raum
       Lehrkraft
     Bei einer Veranstaltung steht oben keine Bezeichnung — die steht nur
     unten in „Information zur Stunde“. Oben erscheinen wie bei einer
     normalen Stunde nur die eingetragenen Klassen und ein eingetragenes
     Fach (mit der Plakette „VERANSTALTUNG“), dann Raum und Lehrkräfte.
     Mehrere Werte stehen in den Details durch „ | “ getrennt (auf den
     Kacheln durch Komma); sehr viele (alle Lehrkräfte, alle Klassen)
     werden auf „… +N“ gekürzt und lassen sich aufklappen. */
  const istGS = c.blockType === 'gs' || c.blockType === 'absence';
  const istVeranstaltung = istGS;
  const zusatz = !!c.extra;
  const SEP = '<span class="sep">|</span>';
  const liste = v => kurzListe(werteListe(v).map(esc), SEP);

  const klassD = Array.isArray(c.classList) && c.classList.length > 1 ? c.classList : null;
  const klassHtml = () => klassD.map(t =>
    (t.state === 'absent') ? `<s>${esc(t.name)}</s>` : esc(t.name)).join('<i>|</i>');
  const klass1 = (c.class || c.classOrig || '').split(',')[0].trim();
  const lehrKurs = werteListe(c.teacher || c.teacherOrig).filter(t => t !== '---')[0] || '';
  const course = c.courseName || [c.subject, klass1, lehrKurs].filter(Boolean).join('_');

  /* Termin ohne Klasse (z. B. Elternsprechtag): die Bezeichnung steht einmal
     als Titel – nicht noch einmal als Fachzeile darunter. */
  const terminOhneKlasse = !istVeranstaltung && c.blockType === 'event' && !c.class && !c.classOrig;
  const head = istVeranstaltung
    ? (c.class ? kurzListe(werteListe(c.class).filter((x, i, a) => a.findIndex(y => y.toUpperCase() === x.toUpperCase()) === i).map(esc), '<i>|</i>') : '')
    : terminOhneKlasse ? esc(c.subject || c.info || 'Termin')
    : (c.class
        ? `${klassD ? klassHtml() : kurzListe(werteListe(c.class).map(esc), '<i>|</i>')}<i>|</i>${esc(course)}`
        : c.classAbsent && c.classOrig
        ? `<s>${werteListe(c.classOrig).map(esc).join('<i>|</i>')}</s><i>|</i>${esc(course)}`
        : esc(course || c.subject || 'Unterricht'));

  const val = key => {
    const now  = detailWert(c[key]);
    const orig = detailWert(c[key + 'Orig']);
    const changed = (c.changed || []).includes(key)
      || (key === 'teacher' && orig !== '' && now !== ''
          && orig.toUpperCase() !== now.toUpperCase());
    if (!now && !orig) return '';
    /* Hinzugekommene Werte mit „+“ davor (wie auf der Kachel). */
    const liste = v => werteListe(v).map(x => esc(plusWert(c, key, x))).join(SEP);
    if (!(changed && orig && orig.toUpperCase() !== now.toUpperCase()))
      return kurzListe(werteListe(now || orig).map(x => esc(plusWert(c, key, x))), SEP);
    if (changed && orig && orig.toUpperCase() !== now.toUpperCase()){
      /* Im Detail stehen keine Plaketten: alt durchgestrichen, daneben neu
         – getrennt durch „|“ („~~LAN~~ | ILK“). Der Pfeil gehört allein zum
         Raum. Eine offene Vertretung steht allein durchgestrichen. */
      const trenn = SEP;
      if (!now) return `<s>${liste(orig)}</s>`;
      return `<s>${liste(orig)}</s>${trenn}${liste(now)}`;
    }
    return liste(now || orig);
  };

  /* Raumzeile: Was weggefallen ist, steht durchgestrichen vor dem Pfeil;
     dahinter alle Räume der Stunde. Ein hinzugefügter Raum lässt die
     bisherigen stehen — dann gibt es nichts durchzustreichen. */
  /* Jeder Raum ist ein eigenes Label. Neu hinzugekommene oder gebuchte
     Räume sind hervorgehoben, die bisherigen stehen unverändert daneben. */
  const raumHtml = () => {
    const jetzt = werteListe(c.room), vorher = werteListe(c.roomOrig);
    const gross = a => a.map(x => x.toUpperCase());
    const weg = vorher.filter(x => !gross(jetzt).includes(x.toUpperCase()));
    const geaendert = (c.changed || []).includes('room');
    void geaendert;
    if (!jetzt.length && vorher.length) return `<s>${vorher.map(esc).join(SEP)}</s>`;
    /* Nur Text, keine Plaketten — mehrere Räume durch „|“ getrennt. */
    const rechts = jetzt.map(x => esc(plusWert(c, 'room', x))).join(SEP);
    if (weg.length) return `<s>${weg.map(esc).join(SEP)}</s><span class="arrow">→</span>${rechts}`;
    return kurzListe(jetzt.map(x => esc(plusWert(c, 'room', x))), SEP);
  };

  const koppD = koppTokens(c);
  const koppHtml = () => koppD.map(t =>
      t.state === 'absent' ? `<s>${esc(t.text)}</s>`
    : t.state === 'sub'    ? (t.for ? `<s>${esc(t.for)}</s>${SEP}${esc(t.text)}` : esc(t.text))
    :                        esc(t.text)).join(SEP);

  /* Entfallene Pausenaufsicht: dasselbe Banner wie eine entfallene Stunde. */
  const banner = c._supCancelled ? 'ENTFÄLLT' : ({ cancelled:'ENTFÄLLT', 'class-absent':'KLASSE ABWESEND' }[c.blockType] || '');
  /* Keine Plaketten im Detail (wie im Original). Jede Angabe steht nur
     einmal: doppelte Lehrkräfte, Klassen oder Räume fallen weg. */
  const einmal = arr => arr.filter((x, i) => arr.findIndex(y => y.toUpperCase() === x.toUpperCase()) === i);
  /* Veranstaltung: nur ein eingetragenes Fach, nie die Bezeichnung (die
     trägt `subject` bei Veranstaltungen). */
  const veranstFach = istVeranstaltung ? String(c.eventSubject || '').trim() : '';
  /* Art der Stunde als graue Pille in Großbuchstaben rechts neben dem Fach
     (Veranstaltung: neben dem Anlass) – wie im Original. Gibt es keine
     solche Zeile, steht sie allein unter der Überschrift. */
  /* Zusätzliche Unterrichtsstunde: Plakette „Zusätzl. Unterrichtsstunde“
     statt eines Vertretungstexts (nie beim Entfall). */
  const artPille = c.blockType === 'klausur' ? 'Prüfung' : istVeranstaltung ? 'Veranstaltung'
                 : (zusatz && c.blockType !== 'cancelled') ? 'Zusätzl. Unterrichtsstunde' : '';
  const pilleHtml = artPille ? `<span class="dpill dpill--art">${esc(artPille.toUpperCase())}</span>` : '';

  /* --- Aktionszeilen ----------------------------------------------------
     Alles, was zum Klassenbuch gehört (online, Raum, Lehrstoff,
     Schüler*innen, Klassenbucheinträge, Notizen für Lehrkräfte), gibt es
     nur für die eigene Stunde mit Unterricht. Fremde Stunden, Zusatz-
     stunden ohne Unterricht und Veranstaltungen zeigen wie in den
     Sollbildern nur Vertretungstext bzw. Information, Hausaufgaben und –
     wenn hinterlegt – die Notizen für Schüler*innen. */
  const off = c.blockType === 'cancelled';
  const onlineD = !!(c.online || cbOnline) && !off && !istVeranstaltung;
  const hatKb = !!(c.unterricht || c.entryId) && !istVeranstaltung;
  const eigen = !state.isStudent && hatKb && cbDarf(c);
  const voll = eigen && !off;
  const rows = [];

  if (voll){
    rows.push({ ico:'online', title:'Findet online statt', key:'online', toggle:!!cbOnline });
    /* Mit eingeschaltetem Schalter: Feld für den Link zur Videokonferenz. */
    if (cbOnline) rows.push({ key:'onlinelink', link: cbOnlineLink });
  }
  /* Schüler*innen (und alle anderen): Online-Unterricht mit Link beitreten. */
  const beitreten = (cbOnline || c.online) ? (cbOnlineLink || c.onlineLink || '') : '';
  if (beitreten && !voll && !off)
    rows.push({ ico:'online', title:'Online-Unterricht beitreten', key:'join', sub: linkKurz(beitreten), link: beitreten });
  if (voll && c.date >= iso(new Date())) rows.push({ ico:'room', key:'room', chips:werteListe(c.room) });
  /* Prüfung: eigene Zeile mit dem Fach, wie im Original. */
  if (c.blockType === 'klausur')
    rows.push({ ico:'exam', title:'Prüfung', key:'exam', sub: c.subject || c.subjectOrig || '' });
  /* Verlegung: Ursprung und Ziel untereinander (fehlt die Gegenseite, nur
     der Titel). Ein Verlegungszeichen gibt es in der PWA nicht. */
  if ((c.moved === 'von' || c.moved === 'nach') && !istVeranstaltung){
    const von = c.movedFrom && c.movedFrom.text, nach = c.movedTo && c.movedTo.text;
    rows.push({ key:'moved', moved:c.moved, title: c.moved === 'von' ? 'Verlegt' : 'Hierher verlegt',
                lines: von && nach ? ['Von: ' + von, 'Nach: ' + nach] : [] });
  }
  if (istVeranstaltung){
    /* Der Infotext der Veranstaltung steht immer als eigene Zeile mit
       Info-Symbol da – auch wenn er dem Anlass im Kopf gleicht. Auf der
       Kachel zeigt das Info-Symbol, dass es ihn gibt. */
    const extraInfo = (c.info || '').trim();
    if (extraInfo) rows.push({ ico:'info', title:'Information zur Stunde', key:'info', sub:extraInfo });
  } else if (c.info){
    /* Zusatzstunde: neben der Plakette nur ein manuell eingegebener
       Vertretungstext (Standardtexte filtert der Server). */
    /* „Vertretungstext" nur bei einer Vertretung oder einem Entfall —
       Klausuren, Raumbuchungen und gewöhnliche Stunden tragen eine
       Information, keinen Vertretungstext. */
    const vertretung = c.blockType === 'change' || c.blockType === 'cancelled';
    rows.push({ ico: vertretung ? 'substitution' : 'info', title: vertretung ? 'Vertretungstext' : 'Information zur Stunde',
                key:'substitution', sub:c.info });
  }
  if (voll){
    rows.push({ ico:'lehrstoff', title:'Lehrstoff', key:'lehrstoff', sub:cbText('lehrstoff') || cbErbe('lehrstoff'),
                done:!!cbText('lehrstoff') });
    rows.push({ ico:'students', title:'Schüler*innen', key:'students',
                count:attAbsentCount(), tone:attAbsentCount() ? 'red' : '',
                checked:attChecked });
  }
  const faellig = Array.isArray(cbFaellig) ? cbFaellig.length : 0;
  rows.push({ ico:'homework', title:'Hausaufgaben', key:'homework',
              count: cbCount('homework') + faellig,
              sub: faellig ? (faellig === 1 ? '1 Aufgabe heute fällig'
                                            : faellig + ' Aufgaben heute fällig') : '' });
  if (voll) rows.push({ ico:'classbook', title:'Klassenbucheinträge', key:'classbook', count:cbCount('classbook') });
  /* Notizen gehören auch zur eigenen ausgefallenen Stunde – dort steht oft
     erst recht etwas zu sagen. */
  if (eigen || cbText('notes_students') || cbErbe('notes_students'))
    rows.push({ ico:'notes_students', title:'Notizen für Schüler*innen', key:'notes_students',
                sub:cbText('notes_students') || cbErbe('notes_students') });
  if (eigen && !state.isStudent)
    rows.push({ ico:'notes_teachers', title:'Notizen für Lehrkräfte', key:'notes_teachers',
                sub:cbText('notes_teachers') || cbErbe('notes_teachers') });

  const zeilen = state.isStudent ? detailFuerSchueler(rows) : rows;
  const ico = r => `<span class="drow2__ico">${DICO[r.ico] || DICO.note}</span>`;
  const PFEIL = '<svg viewBox="0 0 24 24" class="ico drow2__chev"><path d="m9.5 5.5 6.5 6.5-6.5 6.5"/></svg>';

  $('#detailBody').innerHTML = `
    <div class="dhead ${(c.blockType === 'cancelled' || c.blockType === 'class-absent') ? 'is-off' : ''}">
      ${head ? `<h2 class="dtitle">${head}</h2>` : ''}
      ${istVeranstaltung
          /* Veranstaltung: Fachzeile nur mit eingetragenem Fach; die Plakette
             sonst unter den Klassen — ohne Fach und Klassen steht oben nichts. */
          ? (veranstFach ? `<p class="dline dline--big dline--mitpill"><span class="dline__txt">${esc(veranstFach)}</span>${pilleHtml}</p>`
             : head && pilleHtml ? `<p class="dline dline--pill">${pilleHtml}</p>` : '')
          : (c.subject || c.subjectOrig) && !terminOhneKlasse
            ? `<p class="dline dline--big dline--mitpill"><span class="dline__txt">${val('subject')}</span>${pilleHtml}</p>`
            : pilleHtml ? `<p class="dline dline--pill">${pilleHtml}</p>` : ''}
      ${onlineD
          /* Online-Unterricht: kein Raum, nur das Zeichen (wie auf der Kachel). */
          ? `<p class="dline dline--grau dline--online" aria-label="Online-Unterricht">${KACHEL_ONLINE}</p>`
          : (c.room || c.roomOrig) ? `<p class="dline dline--grau">${istVeranstaltung ? kurzListe(einmal(werteListe(c.room)).map(esc), SEP) : raumHtml()}</p>` : ''}
      ${istVeranstaltung
          ? (einmal(werteListe(c.teacher).filter(t => t !== '---')).length ? `<p class="dline dline--grau">${kurzListe(einmal(werteListe(c.teacher).filter(t => t !== '---')).map(esc), SEP)}</p>` : '')
          : (c.teacher || c.teacherOrig)
            ? `<p class="dline dline--grau${koppD ? ' dline--kopp' : ''}">${koppD ? koppHtml() : val('teacher')}</p>` : ''}
    </div>
    ${banner ? `<div class="dbanner">${banner}</div>` : '<div class="detail__sep"></div>'}
    <div class="drows">
      ${zeilen.map(r => r.key === 'onlinelink' ? `
        <div class="drow2 drow2--link" data-key="onlinelink">
          <span class="drow2__ico">${DICO.link}</span>
          <input type="url" class="dlink" id="dOnlineLink" inputmode="url" autocomplete="off" autocapitalize="none" spellcheck="false"
                 enterkeyhint="done" placeholder="Link zum Online-Unterricht (https://…)" value="${esc(r.link || '')}">
          ${r.link ? `<a class="dlink__go" href="${esc(r.link)}" target="_blank" rel="noopener noreferrer" aria-label="Link öffnen">
            <svg viewBox="0 0 24 24" class="ico"><path d="M14 4.5h5.5V10M19.5 4.5 11 13M17.5 13.5v4.2a1.8 1.8 0 0 1-1.8 1.8H6.3a1.8 1.8 0 0 1-1.8-1.8V8.3a1.8 1.8 0 0 1 1.8-1.8h4.2"/></svg></a>` : '<span></span>'}
        </div>` : r.key === 'moved' ? `
        <div class="drow2 drow2--moved" data-key="moved" aria-label="${esc([r.title].concat(r.lines).join(', '))}">
          <span class="drow2__ico">${DICO.moved}</span>
          <span class="drow2__main"><b>${esc(r.title)}</b>${r.lines.map(t => `<span>${esc(t)}</span>`).join('')}</span>
        </div>` : r.key === 'room' ? `
        <div class="drow2 drow2--room" data-key="room">
          ${ico(r)}
          <span class="droom">${r.chips.length ? r.chips.map((x, i) =>
            `<button type="button" class="droom__chip" data-chip="${i}">${esc(x)}</button>`).join('')
            : '<button type="button" class="droom__chip droom__neu">Raum hinzufügen</button>'}</span>
          ${r.chips.length ? `<button type="button" class="droom__add" aria-label="Raum hinzufügen">
            <svg viewBox="0 0 24 24" class="ico"><circle cx="12" cy="12" r="9.2"/><path d="M12 7.6v8.8M7.6 12h8.8"/></svg>
          </button>` : '<span></span>'}
        </div>` : `
        <div class="drow2" data-key="${r.key || ''}" role="button">
          ${ico(r)}
          <span class="drow2__main"><b>${esc(r.title)}</b>${r.sub ? `<span>${esc(r.sub)}</span>` : ''}</span>
          <span class="drow2__end">
            ${r.toggle !== undefined
              ? `<span class="dtoggle ${r.toggle ? 'is-on' : ''}" role="switch" aria-checked="${r.toggle ? 'true' : 'false'}"></span>`
              : `${r.done ? '<svg viewBox="0 0 24 24" class="ico drow2__done"><circle cx="12" cy="12" r="9"/><path d="m8.2 12.3 2.6 2.6L16 9.8"/></svg>' : ''}
                 ${r.count !== undefined ? `<span class="drow2__count ${r.tone === 'red' ? 'is-red' : ''} ${r.checked ? 'is-checked' : ''}">${r.count}</span>` : ''}
                 ${PFEIL}`}
          </span>
        </div>`).join('')}
    </div>`;
  kurzListenBinden($('#detailBody'));

  $$('#detailBody .drow2').forEach(el => {
    const key = el.dataset.key;
    if (!key || key === 'room' || key === 'moved') return;
    if (key === 'online'){
      el.addEventListener('click', () => onlineUmschalten(c));
      return;
    }
    if (key === 'onlinelink'){
      onlineLinkBinden(c, el);
      return;
    }
    if (key === 'join'){
      el.addEventListener('click', () => {
        const l = (zeilen.find(r => r.key === 'join') || {}).link;
        if (l) window.open(l, '_blank', 'noopener');
      });
      return;
    }
    if (state.isStudent && !['substitution', 'info', 'exam', 'homework', 'lehrstoff', 'notes_students'].includes(key)) return;
    if (el.classList.contains('is-readonly')) return;
    el.addEventListener('click', () => openClassbook(key));
  });
  raumZeileBinden(c);

  /* Nach jedem Aufbau erneut: die Zeilen entstehen hier neu, ein einmaliges
     Ausblenden beim Start hielte nicht. */
  if (state.isStudent) rechteAnwenden();
}

/* Symbole der Aktionszeilen – nachgezeichnet nach den Sollbildern. */
const DICO = (() => {
  /* Alle Zeichen auf demselben 28er-Raster mit derselben Strichstärke
     (1,5 Einheiten) — die Symbole der Sammlung sind als Umriss mit dieser
     Stärke gezeichnet, die eigenen als Linie mit genau diesem Wert. Die
     Notiz lag vorher auf einem 32er-Raster und wirkte dadurch dünner, die
     selbst gezeichneten (Hausaufgaben, Raum, Info) dicker. */
  const u = id => `<svg viewBox="0 0 28 28" class="uic ico dico dico--sym"><use href="#${id}"></use></svg>`;
  const linie = (inner, vb) => `<svg viewBox="${vb || '0 0 28 28'}" class="ico dico dico--linie" fill="none" stroke="currentColor" stroke-linecap="round" stroke-linejoin="round">${inner}</svg>`;
  /* Notiz der Sammlung, auf das 28er-Raster zugeschnitten (32er-Vorlage). */
  const notiz = linie('<path d="M26.5 25.75a1.5 1.5 0 0 1-1.5 1.5H7a1.5 1.5 0 0 1-1.5-1.5V6.25A1.5 1.5 0 0 1 7 4.75h15.045c.392 0 .768.153 1.048.426l2.954 2.883c.29.282.453.67.453 1.074V25.75zm-16.455-13.5h12m-12 4.5h12m-12 4.5h6"/>', '2 2 28 28');
  return {
    note:          notiz,
    substitution:  notiz,
    lehrstoff:     notiz,
    notes_students:notiz,
    notes_teachers:notiz,
    online:        u('ic-online-lesson'),
    students:      u('ic-lessons-classes'),
    homework:      u('ic-homework'),
    classbook:     u('ic-navigation-class-register'),
    /* Info zur Stunde: normal gezeichnet — Kreis mit „i“ als Linienzeichen
       wie die übrigen Zeilen, keine gefüllte Scheibe mit ausgespartem „i“
       (die wirkte, besonders im Dunkelmodus, invertiert). */
    info:          linie('<circle cx="14" cy="14" r="10.25"/><path d="M14 12.6v7"/><circle cx="14" cy="8.9" r=".55" fill="currentColor"/>'),
    room:          u('ic-shared-roomentity'),
    exam:          u('ic-examinations'),
    /* Verlegung: Pfeil nach rechts (Ursprung → Ziel) */
    moved:         linie('<path d="M4.5 14h17.5M16.5 8.5 22 14l-5.5 5.5"/>'),
    link:          linie('<path d="M12.2 15.8a4.6 4.6 0 0 0 6.5 0l3.6-3.6a4.6 4.6 0 0 0-6.5-6.5l-1.5 1.5"/><path d="M15.8 12.2a4.6 4.6 0 0 0-6.5 0l-3.6 3.6a4.6 4.6 0 0 0 6.5 6.5l1.5-1.5"/>')
  };
})();

/* =====================================================================
   Klassenbuch — Vertretungstext, Lehrstoff, Notizen, Anwesenheit
   ---------------------------------------------------------------------
   Gespeichert wird über api/classbook (Klassenbuch von UntPlanWeb). Ist der Endpunkt nicht
   erreichbar, hält der Gerätespeicher die Eingabe fest, damit nichts
   verloren geht.
   ===================================================================== */
const CB_URL = new URL('api/classbook', location.href).toString();

const CB = {
  lehrstoff:      { title:'Lehrstoff',                 kind:'text' },
  notes_students: { title:'Notizen für Schüler*innen', kind:'text' },
  notes_teachers: { title:'Notizen für Lehrkräfte',    kind:'text' },
  homework:       { title:'Hausaufgaben',              kind:'list' },
  classbook:      { title:'Klassenbucheinträge',       kind:'list' },
  students:       { title:'Schüler*innen',             kind:'students' },
  substitution:   { title:'Vertretungstext',           kind:'readonly' },
  info:           { title:'Information zur Stunde',    kind:'readonly' },
  exam:           { title:'Prüfung',                   kind:'readonly' },
  room:           { title:'Raum ersetzen',             kind:'room' }
};

/* =====================================================================
   Raumbuchung mobil (Raum-Chip in den Stundendetails)
   ---------------------------------------------------------------------
   Jeder Raum der Stunde steht als Chip in einer eigenen Zeile. Tippen
   öffnet ein kleines Menü „Ersetzen“ / „Löschen“, das „+“ rechts fügt
   einen zusätzlichen Raum hinzu. Ersetzen und Hinzufügen führen in die
   Raumliste („R1BK ersetzen“) mit Suchfeld und Filter nach Abteilung
   (Raumart). Gespeichert wird über api/rooms (action=change) als
   Raumänderung in UntPlanWeb; danach steht sie sofort im Plan.
   ===================================================================== */
const ROOM_URL = new URL('api/rooms', location.href).toString();
let raumStand = { c:null, op:'', alt:'', liste:[], suche:'', abteilung:'' };

function raumMenuZu(){
  const m = document.getElementById('droomMenu');
  if (m) m.remove();
}

function raumZeileBinden(c){
  raumMenuZu();
  const zeile = $('#detailBody .drow2--room');
  if (!zeile) return;
  const chips = werteListe(c.room);
  zeile.querySelectorAll('[data-chip]').forEach(b => b.addEventListener('click', e => {
    e.stopPropagation();
    const offen = document.getElementById('droomMenu');
    if (offen && offen.dataset.chip === b.dataset.chip){ raumMenuZu(); return; }
    raumMenuZu();
    const raum = chips[+b.dataset.chip];
    const m = document.createElement('div');
    m.className = 'droom__menu'; m.id = 'droomMenu'; m.dataset.chip = b.dataset.chip;
    m.setAttribute('role', 'menu');
    m.innerHTML = `<button type="button" role="menuitem" data-op="replace">Ersetzen</button>
                   <button type="button" role="menuitem" data-op="remove">Löschen</button>`;
    const host = $('#detailBody');
    const hr = host.getBoundingClientRect(), br = b.getBoundingClientRect();
    m.style.left = Math.max(12, br.left - hr.left) + 'px';
    m.style.top = (br.bottom - hr.top + host.scrollTop + 4) + 'px';
    host.appendChild(m);
    m.querySelector('[data-op="replace"]').addEventListener('click', ev => {
      ev.stopPropagation(); raumMenuZu(); raumListeOeffnen(c, 'replace', raum);
    });
    m.querySelector('[data-op="remove"]').addEventListener('click', async ev => {
      ev.stopPropagation(); raumMenuZu();
      await raumAendern(c, 'remove', raum, '');
    });
  }));
  zeile.querySelectorAll('.droom__add, .droom__neu').forEach(plus =>
    plus.addEventListener('click', e => { e.stopPropagation(); raumMenuZu(); raumListeOeffnen(c, 'add', ''); }));
}
document.addEventListener('click', e => {
  if (!e.target.closest('#droomMenu') && !e.target.closest('[data-chip]')) raumMenuZu();
});

async function raumListeOeffnen(c, op, alt){
  raumStand = { c, op, alt, liste:[], suche:'', abteilung:'' };
  cbCurrent = 'room';
  $('#dsubTitle').textContent = op === 'replace' ? `${alt} ersetzen` : 'Raum hinzufügen';
  $('#dsubWhen').hidden = true;
  $('#dsubSave').hidden = true;
  $('#dsubAddTop').hidden = true;
  $('#dsubBack').innerHTML = '<svg viewBox="0 0 24 24" class="ico"><path d="m14 6-6 6 6 6"/></svg>';
  $('#detailSub').classList.add('dsub--raum');
  $('#detailSub').hidden = false;
  const body = $('#dsubBody');
  body.innerHTML = '<p class="dsub__read">Freie Räume werden gesucht …</p>';
  const bis = c.lesson + Math.max(1, c.span || 1) - 1;
  try {
    const r = await fetch(`${ROOM_URL}?action=free&date=${c.date}&from=${c.lesson}&to=${bis}${c.unterricht ? `&lesson=${c.unterricht}` : ''}`, { credentials:'same-origin' });
    const j = await r.json();
    if (!r.ok || !j.ok) throw new Error(j.error || 'Fehler ' + r.status);
    const belegt = werteListe(c.room).map(x => x.toUpperCase());
    raumStand.liste = (j.rooms || []).filter(x => !belegt.includes(String(x.code).toUpperCase()));
    raumListeZeichnen();
  } catch (e){ body.innerHTML = cbEmpty('Räume nicht erreichbar: ' + e.message); }
}

function raumListeZeichnen(){
  const st = raumStand;
  const body = $('#dsubBody');
  const q = st.suche.trim().toLowerCase();
  const treffer = st.liste.filter(x =>
    (!st.abteilung || x.kind === st.abteilung) &&
    (!q || String(x.code).toLowerCase().includes(q) || String(x.name || '').toLowerCase().includes(q)));
  const TUER = '<svg viewBox="0 0 28 28" class="uic ico rzeile__sym"><use href="#ic-shared-roomentity"></use></svg>';
  const neu = !body.querySelector('.rsuche');
  if (neu){
    body.innerHTML = `
      <label class="rsuche">
        <svg viewBox="0 0 24 24" class="ico"><circle cx="10.8" cy="10.8" r="6.2"/><path d="m15.4 15.4 4.4 4.4"/></svg>
        <input type="search" id="raumSuche" placeholder="Suchen" autocomplete="off" enterkeyhint="search">
        <button type="button" class="rsuche__x" id="raumSucheX" aria-label="Suche leeren">
          <svg viewBox="0 0 24 24" class="ico"><path d="m7 7 10 10M17 7 7 17"/></svg>
        </button>
      </label>
      <div class="rfilter"><button type="button" class="rpill" id="raumAbteilung"></button></div>
      <div class="rliste" id="raumListe"></div>`;
    $('#raumSuche').addEventListener('input', e => { raumStand.suche = e.target.value; raumListeZeichnen(); });
    $('#raumSucheX').addEventListener('click', () => { raumStand.suche = ''; $('#raumSuche').value = ''; raumListeZeichnen(); });
    $('#raumAbteilung').addEventListener('click', e => { e.stopPropagation(); raumAbteilungWaehlen(); });
  }
  const pill = $('#raumAbteilung');
  pill.textContent = st.abteilung || 'Abteilung';
  pill.classList.toggle('is-on', !!st.abteilung);
  $('#raumListe').innerHTML = treffer.length
    ? treffer.map(x => `
        <button type="button" class="rzeile" data-raum="${esc(x.code)}">
          <span class="rzeile__ico">${TUER}</span>
          <span class="rzeile__main"><b>${esc(x.code)}</b>${x.name && x.name !== x.code ? `<span>${esc(x.name)}</span>` : ''}</span>
        </button>`).join('')
    : cbEmpty(st.liste.length ? 'Kein Raum gefunden' : 'Kein buchbarer Raum frei');
  $$('#raumListe [data-raum]').forEach(b => b.addEventListener('click', async () => {
    b.disabled = true;
    const ok = await raumAendern(st.c, st.op, b.dataset.raum, st.alt);
    if (!ok) b.disabled = false;
  }));
}

function raumAbteilungWaehlen(){
  const alt = document.getElementById('raumAbtMenu');
  if (alt){ alt.remove(); return; }
  const arten = [...new Set(raumStand.liste.map(x => x.kind).filter(Boolean))].sort((a, b) => a.localeCompare(b, 'de'));
  const m = document.createElement('div');
  m.className = 'droom__menu rabt'; m.id = 'raumAbtMenu';
  m.innerHTML = ['', ...arten].map(a =>
    `<button type="button" data-a="${esc(a)}" class="${a === raumStand.abteilung ? 'is-on' : ''}">${esc(a || 'Alle Abteilungen')}</button>`).join('');
  const pill = $('#raumAbteilung');
  const host = $('#dsubBody');
  const hr = host.getBoundingClientRect(), pr = pill.getBoundingClientRect();
  m.style.left = (pr.left - hr.left) + 'px';
  m.style.top = (pr.bottom - hr.top + host.scrollTop + 4) + 'px';
  host.appendChild(m);
  m.querySelectorAll('[data-a]').forEach(b => b.addEventListener('click', e => {
    e.stopPropagation();
    raumStand.abteilung = b.dataset.a;
    m.remove();
    raumListeZeichnen();
  }));
}
document.addEventListener('click', e => {
  const m = document.getElementById('raumAbtMenu');
  if (m && !e.target.closest('#raumAbtMenu')) m.remove();
});

/* Raum ersetzen, hinzufügen oder löschen – danach Woche neu laden und die
   Stunde mit dem neuen Stand wieder zeigen. */
async function raumAendern(c, op, raum, alt){
  const bis = c.lesson + Math.max(1, c.span || 1) - 1;
  try {
    const f = new URLSearchParams({ action:'change', op, date:c.date, from:String(c.lesson), to:String(bis), room:raum, old:alt || '',
                                    class:c.class || c.classOrig || '', subject:c.subject || '',
                                    unterricht:String(c.unterricht || c.entryId || '') });
    const a = await fetch(ROOM_URL, { method:'POST', credentials:'same-origin', headers:{ 'Content-Type':'application/x-www-form-urlencoded' }, body:f });
    const aj = await a.json().catch(() => ({}));
    if (!a.ok || !aj.ok) throw new Error(aj.error || 'Fehler ' + a.status);
    toast(op === 'remove' ? `Raum ${raum} gelöscht` : op === 'add' ? `Raum ${raum} hinzugefügt` : `${alt} durch ${raum} ersetzt`);
    $('#detailSub').hidden = true;
    $('#detailSub').classList.remove('dsub--raum');
    cbCurrent = null;
    /* Die Stunde sofort mit den neuen Räumen zeigen, der Plan lädt nach. */
    const neu = Array.isArray(aj.rooms) ? aj.rooms : [];
    const vorher = werteListe(c._raeumeVorher != null ? c._raeumeVorher : (c.roomOrig || c.room));
    if (c._raeumeVorher == null) c._raeumeVorher = c.roomOrig || c.room || '';
    c.room = neu.join(',');
    c.roomOrig = vorher.filter(x => !neu.some(y => y.toUpperCase() === x.toUpperCase())).join(',');
    c.roomNew  = neu.filter(x => !vorher.some(y => y.toUpperCase() === x.toUpperCase())).join(',');
    if (!(c.changed || []).includes('room') && !aj.reset) c.changed = [...(c.changed || []), 'room'];
    renderDetail();
    wochenVergessen();
    planNachladenNachRaum(c);
    return true;
  } catch (e){
    toast('Nicht gespeichert: ' + e.message);
    return false;
  }
}

async function planNachladenNachRaum(c){
  try {
    await refresh();
    const neu = ((state.week && state.week.byDate) || {})[c.date] || [];
    const treffer = neu.find(x => x.lesson === c.lesson && (x.unterricht || x.entryId) === (c.unterricht || c.entryId));
    if (treffer && state.detail === c && !$('#sheetDetail').hidden){
      state.detail = treffer; state.detailFull = treffer;
      renderDetail();
    }
  } catch (e){ /* der Stand in den Details bleibt */ }
}

const cbLocalKey = (c, key) => `cb.${c.date}.${c.lesson}${c._teil ? 'e' : ''}.${key}`;
let cbCache = {};                 /* Inhalte der geöffneten Stunde */
let cbTeil = null;                /* Ganzer Block / Einzelstunde (lessonPart vom Server) */
let attWegKlassen = [];           /* Klassen mit Abwesenheit in dieser Stunde */
let cbFaellig = [];               /* heute fällige Hausaufgaben */
let cbCurrent = null;
let cbEdit = null;                /* eigene Stunde? (null = noch nicht geladen) */
let cbOnline = false;             /* „Findet online statt“ */
let cbOnlineLink = '';            /* Link zum Online-Unterricht */


/* Link zum Online-Unterricht kurz: nur der Rechnername. */
function linkKurz(l){
  try { const u = new URL(l); return u.host + (u.pathname.length > 1 ? '/…' : ''); } catch (e){ return String(l || ''); }
}

/* Feld für den Link: gespeichert wird beim Verlassen und mit der Eingabetaste. */
function onlineLinkBinden(c, row){
  const inp = row.querySelector('input');
  if (!inp) return;
  let alt = inp.value.trim();
  const sichern = async () => {
    let wert = inp.value.trim();
    if (wert === alt) return;
    if (wert && !/^[a-z][a-z0-9+.-]*:\/\//i.test(wert)) wert = 'https://' + wert;
    if (wert && !/^https?:\/\/[^\s]+\.[^\s]+/i.test(wert)){ toast('Bitte einen gültigen Link (https://…) eingeben'); return; }
    alt = wert;
    try {
      const r = await cbPost(c, { action:'online', value:'1', link: wert });
      cbOnlineLink = r.onlineLink !== undefined ? r.onlineLink : wert;
      [c, state.detail, state.detailFull].filter(Boolean).forEach(x => { x.online = true; x.onlineLink = cbOnlineLink || null; });
      toast(cbOnlineLink ? 'Link gespeichert' : 'Link entfernt');
      renderDetail();
      wochenVergessen(); planNachladen();
    } catch (e){ toast('Nicht gespeichert: ' + e.message); }
  };
  inp.addEventListener('keydown', e => { if (e.key === 'Enter'){ e.preventDefault(); inp.blur(); } });
  inp.addEventListener('blur', sichern);
  inp.addEventListener('click', e => e.stopPropagation());
}

/* Eigene Stunde: Das sagt UntPlanWeb beim Laden. Bis dahin gilt als eigen,
   was die eigene Lehrkraft trägt – so stehen die Zeilen gleich da. */
function cbDarf(c){
  if (cbEdit !== null) return cbEdit;
  const ich = String(state.ownCode || '').toUpperCase();
  return !!ich && werteListe(c.teacher).some(t => t.toUpperCase() === ich);
}

/* „Findet online statt“ – Schalter in den Stundendetails. */
async function onlineUmschalten(c){
  const neu = !cbOnline;
  cbOnline = neu;
  renderDetail();
  /* Eingeschaltet ohne Link: gleich ins Feld für den Link. */
  if (neu && !cbOnlineLink) setTimeout(() => { const f = $('#dOnlineLink'); if (f) f.focus(); }, 60);
  try {
    const r = await cbPost(c, { action:'online', value: neu ? '1' : '0' });
    if (r && r.onlineLink !== undefined) cbOnlineLink = r.onlineLink || '';
    [c, state.detail, state.detailFull].filter(Boolean).forEach(x => { x.online = neu || null; if (!neu) x.onlineLink = null; });
    wochenVergessen(); planNachladen();
  }
  catch (e){ cbOnline = !neu; renderDetail(); toast('Nicht gespeichert: ' + e.message); }
}

function cbParams(c, extra){
  /* Die Kennung der Stunde wird mitgeschickt, damit der Server sie nicht
     aus Datum, Stunde und Klasse erraten muss — sonst könnte er bei
     mehreren Einträgen im selben Zeitfenster den falschen erwischen und
     die Notiz landete an einer Stunde, die der Desktop nicht anzeigt. */
  /* Bei einer Kopplung stehen mehrere Klassen auf der Kachel. Der Server
     erwartet eine — genommen wird die erste; die Stunde selbst ist über
     `entry` ohnehin eindeutig bestimmt. */
  const p = { date: c.date, lesson: String(c.lesson),
              class: (c.class || c.classOrig || '').split(',')[0].trim() };
  if (c.entryId) p.entry = String(c.entryId);
  /* UntPlanWeb führt das Klassenbuch je Unterricht — die Kennung gilt für
     jede Stunde des Blocks, auch für eine einzeln gewählte. */
  if (c.unterricht) p.unterricht = String(c.unterricht);
  /* Kopplung mit mehreren Lehrkräften (z. B. KR/ER): die Lehrkraft der Kachel mitgeben, damit der Eintrag an der richtigen Stunde landet. */
  const lk = String(c.teacher || '').split(',').map(x => x.trim()).filter(Boolean);
  if (lk.length === 1) p.teacher = lk[0];
  /* Gilt die Eintragung für einen Block, bekommt der Server dessen Länge
     und schreibt in jede Stunde — genauso wie die Desktop-Version. */
  if (!c._teil && (c.span || 1) > 1) p.span = String(c.span);
  /* Einzelstunde eines Blocks (Vertrag Einzelstunde): Lehrstoff, Notizen,
     Online, Hausaufgaben und Abwesenheiten gelten dann nur für diese Stunde. */
  if (c._teil) p.einzelstunde = String(c._teil);
  return new URLSearchParams(Object.assign(p, extra || {}));
}

async function cbLoadAll(c){
  cbCache = {};
  cbTeil = null;
  cbEdit = null; cbOnline = !!c.online; cbOnlineLink = c.onlineLink || '';
  /* Zuerst der Gerätespeicher, damit sofort etwas dasteht. */
  Object.keys(CB).forEach(k => {
    const v = store.get(cbLocalKey(c, k), null);
    if (v !== null) cbCache[k] = v;
  });
  try {
    /* Ein Zug für alles: Inhalte, Schülerliste, Kontrollstand und die
       Abwesenheitsgründe. Danach steht die Stunde genau so da wie am
       Desktop, und die Unteransichten öffnen ohne Nachladen. */
    const res = await fetch(CB_URL + '?' + cbParams(c, { action:'all' }), { credentials:'same-origin' });
    const data = await res.json();
    if (!data || !data.ok) return cbCache;
    /* Inzwischen eine andere Stunde offen? Dann gehört das hier nicht mehr. */
    if (state.detail && attKey(state.detail) !== attKey(c)) return cbCache;

    cbCache = Object.assign(cbCache, data.data);
    /* Ganzer Block / Einzelstunde: Stunden des Blocks (mit Kennzeichen für
       eigene Einträge) und in der Einzelstunde der geerbte Blockeintrag. */
    cbTeil = data.lessonPart || null;
    if (typeof data.canEdit === 'boolean') cbEdit = data.canEdit;
    cbOnline = !!data.online;
    cbOnlineLink = data.onlineLink || '';
    /* Der Gerätespeicher wird auf den Stand des Servers gebracht — sonst
       blitzt beim nächsten Öffnen wieder der alte Text auf. */
    Object.keys(data.data).forEach(k2 => store.set(cbLocalKey(c, k2), data.data[k2]));

    if (Array.isArray(data.students)){
      attList    = data.students;
      attChecked = !!data.checked;
      attAbsent  = +data.absent || 0;
      /* Klassen, die zu dieser Stunde gar nicht da sind. Ihre Kinder
         stehen nicht in der Liste — das gehört dazugesagt. */
      attWegKlassen = Array.isArray(data.absentClasses) ? data.absentClasses : [];
    }
    if (Array.isArray(data.dueHomework)){
      /* Was zu dieser Stunde abzugeben ist. Steht an einer anderen
         Stundenzeile und käme sonst nirgends vor. */
      cbFaellig = data.dueHomework;
    }
    if (Array.isArray(data.reasons) && data.reasons.length)
      absReasons = [{ code:'', name:'Ohne Angabe' }].concat(data.reasons);
  } catch (e){ /* offline — der Gerätespeicher genügt */ }
  return cbCache;
}

const cbText  = key => (typeof cbCache[key] === 'string' ? cbCache[key] : '');
/* In einer Einzelstunde gilt der Eintrag des ganzen Blocks mit — er steht
   (nur lesend) da, solange die Stunde keinen eigenen hat. */
const cbErbe = key => {
  const e = state.detail && state.detail._teil && cbTeil && cbTeil.inherited;
  const t = e && typeof e[key] === 'string' ? e[key].trim() : '';
  return t ? 'Ganzer Block: ' + t : '';
};
const cbCount = key => (Array.isArray(cbCache[key]) ? cbCache[key].length : 0);

async function cbPost(c, body){
  /* Schüler*innen tragen nichts ein — der Server weist es ohnehin ab,
     aber es soll gar nicht erst danach aussehen. */
  if (state.isStudent) throw new Error('keine Berechtigung');
  /* Ein einzelner Aussetzer der Verbindung soll nicht gleich als „nicht
     gespeichert" gemeldet werden — einmal wird still nachgefasst. */
  let res = null, netzFehler = null;
  for (let versuch = 0; versuch < 2; versuch++){
    try {
      res = await fetch(CB_URL, {
        method:'POST', credentials:'same-origin', cache:'no-store',
        headers:{ 'Content-Type':'application/x-www-form-urlencoded' },
        body: cbParams(c, body)
      });
      netzFehler = null;
      break;
    } catch (e){
      netzFehler = e;
      await new Promise(r => setTimeout(r, 400));
    }
  }
  if (netzFehler) throw new Error('keine Verbindung');

  const data = await res.json().catch(() => null);
  if (!data || !data.ok){
    if (data && data.offline) throw new Error('keine Verbindung');
    throw new Error((data && (data.detail || data.error)) || 'Fehler');
  }
  /* Landet etwas nur auf dem Gerät, muss man das wissen — sonst hält man
     es für gespeichert und am Desktop steht nichts. */
  if (data.local) toast('Nur auf diesem Gerät gespeichert — die Stunde fehlt im Stundenplan');
  return data;
}

/* Eine gespeicherte Notiz zeigt ihr Info-Zeichen sofort auf der Kachel —
   noch bevor das Detailfenster schließt. Gesetzt wird an der Kachel des
   Plans (dem ganzen Block) und an der gewählten Einzelstunde; danach
   gleicht der Plan still mit dem Server ab. */
function karteNotizSetzen(c, key, value){
  const feld = key === 'notes_students' ? 'noteStudents' : key === 'notes_teachers' ? 'noteTeachers' : '';
  if (!feld) return;
  const v = String(value || '').trim();
  const ziele = new Set([c, state.detail, state.detailFull].filter(Boolean));
  const tag = ((state.allByDate || (state.week && state.week.byDate) || {})[c.date]) || [];
  const kennung = c.unterricht || c.entryId;
  tag.forEach(x => {
    if (x.lesson <= c.lesson + (c.span || 1) - 1 && c.lesson <= x.lesson + (x.span || 1) - 1
        && kennung && (x.unterricht || x.entryId) === kennung) ziele.add(x);
  });
  ziele.forEach(x => {
    x[feld] = v || null;
    x.hasNote = !!(x.noteStudents || x.noteTeachers) || null;
  });
  const top = $('#gridScroll').scrollTop;
  renderGrid();
  $('#gridScroll').scrollTop = top;
  if (state.screen === 'start') renderStart();
}

async function cbSaveText(c, key, value){
  cbCache[key] = value;
  store.set(cbLocalKey(c, key), value);            /* Rückhalt auf dem Gerät */
  try {
    await cbPost(c, { action:'save', field:key, value });
    karteNotizSetzen(c, key, value);
    wochenVergessen();
    planNachladen();
  }
  catch (e){
    /* Konnte der Server es nicht übernehmen, darf auf dem Gerät nichts
       Abweichendes stehen bleiben — sonst sieht es hier gespeichert aus
       und am Desktop fehlt es. */
    store.set(cbLocalKey(c, key), null);
    toast('Nicht gespeichert: ' + e.message);
    await cbLoadAll(c);
    if (state.detail === c) renderDetail();
  }
}

/* ---------------------------------------------------------------------
   Unteransicht
   --------------------------------------------------------------------- */
/* Leere Ansicht wie in der Vorlage: Sinnbild, Überschrift, Hinweis. */
const cbEmpty = titel => `
  <div class="dsub__empty">
    <span class="dsub__empty-ico" aria-hidden="true">
      <svg viewBox="0 0 24 24">
        <ellipse cx="12" cy="6.6" rx="7.2" ry="2.8"/>
        <path d="M4.8 6.6v10.8c0 1.6 3.2 2.8 7.2 2.8s7.2-1.2 7.2-2.8V6.6"/>
        <path d="M4.8 12c0 1.6 3.2 2.8 7.2 2.8s7.2-1.2 7.2-2.8"/>
      </svg>
    </span>
    <b>${esc(titel)}</b>
    <span>Derzeit gibt es hier noch nichts</span>
  </div>`;

function cbItemRow(key, o, i){
  /* Gilt eine Hausaufgabe nur für eine Stunde des Blocks bzw. (in einer
     Einzelstunde) für den ganzen Block, steht das dabei. */
  const geltung = key !== 'homework' || !state.detailFull || (state.detailFull.span || 1) < 2 ? ''
    : (o.part ? o.part + '. Stunde' : (state.detail && state.detail._teil ? 'Ganzer Block' : ''));
  const zeile2 = key === 'homework'
    ? [geltung, o.due ? 'fällig ' + deDate(o.due) : '', o.note || ''].filter(Boolean).join(' · ')
    : [o.cat || '', o.at ? deDate(o.at.slice(0, 10)) : ''].filter(Boolean).join(' · ');
  return `
    <div class="dsub__item ${o.done ? 'is-done' : ''}" data-i="${i}">
      <span class="dsub__item-main">
        <b>${esc(o.text || '')}</b>
        ${zeile2 ? `<span>${esc(zeile2)}</span>` : ''}
      </span>
      ${state.isStudent ? '' : `<button data-del="${i}" aria-label="Löschen">
        <svg viewBox="0 0 24 24"><path d="m6 6 12 12M18 6 6 18"/></svg>
      </button>`}
    </div>`;
}

const deDate = s => {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(String(s || ''));
  return m ? `${m[3]}.${m[2]}.${m[1]}` : String(s || '');
};

function openClassbook(key){
  const c = state.detail;
  if (!c || !CB[key]) return;
  cbCurrent = key;

  const d = parseISO(c.date);
  const two = n => String(n).padStart(2, '0');
  const t1 = (state.times[c.lesson] || {}).start || '';
  const t2 = (state.times[Math.min(c.lesson + c.span - 1, 12)] || {}).end || '';

  $('#dsubTitle').textContent = CB[key].title;
  $('#dsubWhen').textContent  =
    `${two(d.getDate())}.${two(d.getMonth()+1)}.${d.getFullYear()}, ${t1.replace(/^0/, '')} - ${t2.replace(/^0/, '')}`;
  $('#dsubSave').hidden = !(CB[key].kind === 'text' || CB[key].kind === 'students');
  /* In der Schülerliste steht rechts oben der Besen: alle anwesend. */
  $('#dsubSave').classList.toggle('is-besen', CB[key].kind === 'students');
  $('#dsubSave').setAttribute('aria-label', CB[key].kind === 'students' ? 'Alle als anwesend markieren' : 'Speichern');
  $('#dsubSave').innerHTML = CB[key].kind === 'students' ? ATT_BESEN
    : '<svg viewBox="0 0 24 24" class="ico"><path d="m5 12.5 4.5 4.5L19 7"/></svg>';
  $('#dsubAddTop').hidden = CB[key].kind !== 'list';
  /* Suche unter dem Kopf — nur bei den Klassenbucheinträgen. */
  const sucheBox = $('#dsubSucheBox');
  if (sucheBox){
    const mitSuche = CB[key].kind === 'list' && key === 'classbook';
    if (sucheBox.hidden === mitSuche || !mitSuche) $('#dsubSuche').value = '';
    sucheBox.hidden = !mitSuche;
  }
  if (state.isStudent) rechteAnwenden();
  /* Reine Textansichten schließen mit dem Kreuz, Listen führen zurück. */
  const zu = CB[key].kind === 'readonly' || CB[key].kind === 'text';
  $('#dsubBack').innerHTML = zu
    ? '<svg viewBox="0 0 24 24" class="ico"><path d="m6 6 12 12M18 6 6 18"/></svg>'
    : '<svg viewBox="0 0 24 24" class="ico"><path d="m14 6-6 6 6 6"/></svg>';
  $('#dsubWhen').hidden = CB[key].kind === 'readonly';

  const body = $('#dsubBody');

  if (CB[key].kind === 'readonly'){
    const txt = key === 'exam'
      ? [longName('subject', c.subject || c.subjectOrig || ''), (c.info || '').trim()].filter(Boolean).join('\n')
      : key === 'info' ? (c.info || c.subject || '') : c.info;
    body.innerHTML = txt
      ? `<p class="dsub__read">${esc(txt)}</p>`
      : cbEmpty(key === 'info' ? 'Keine Information' : key === 'exam' ? 'Keine Angaben' : 'Kein Vertretungstext');
  }

  if (CB[key].kind === 'text'){
    /* Schüler*innen lesen nur: kein Schreibrecht, keine Tastatur, kein
       leeres Feld mit Aufforderung zum Eintippen. */
    /* Lehrstoff und Notizen lassen sich für jede Stunde eintragen —
       vergangene wie kommende, ohne Begrenzung auf Wochen oder Daten. */
    const nurLesen = state.isStudent;
    const txt = cbText(key);
    body.innerHTML = (nurLesen
      ? (txt.trim() ? `<p class="dsub__read">${esc(txt)}</p>` : cbEmpty('Nichts hinterlegt'))
      : `<textarea class="dsub__area" id="dsubText"
           placeholder="Text hier eingeben">${esc(txt)}</textarea>`);
    $('#dsubSave').hidden = nurLesen;
    if (!nurLesen) setTimeout(() => $('#dsubText') && $('#dsubText').focus(), 120);
  }

  if (CB[key].kind === 'list'){
    const items = Array.isArray(cbCache[key]) ? cbCache[key] : [];

    /* Bei den Hausaufgaben kommt dazu, was **zu** dieser Stunde fällig
       ist. Diese Aufgaben stehen an einer anderen Stundenzeile — der, in
       der sie aufgegeben wurden — und lassen sich von hier aus nicht
       löschen oder ändern; sie stehen darum in einem eigenen Abschnitt
       darüber, mit dem Tag ihrer Aufgabe. */
    const faellig = (key === 'homework' && Array.isArray(cbFaellig)) ? cbFaellig : [];
    const faelligHtml = faellig.length ? `
      <div class="dsub__gruppe">Heute fällig</div>
      <div class="dsub__list">${faellig.map(o => `
        <div class="dsub__item is-fremd">
          <span class="dsub__item-main">
            <b>${esc(o.text || '')}</b>
            ${(o.since || o.note) ? `<span>${esc([
                o.since ? 'aufgegeben ' + deDate(o.since) : '', o.note || ''
              ].filter(Boolean).join(' · '))}</span>` : ''}
          </span>
        </div>`).join('')}</div>` : '';

    const eigenHtml = items.length
      ? (faellig.length ? '<div class="dsub__gruppe">In dieser Stunde aufgegeben</div>' : '')
        + `<div class="dsub__list">${items.map((t, i) => cbItemRow(key, itemParse(t), i)).join('')}</div>`
      : '';

    body.innerHTML = (faelligHtml + eigenHtml) ||
      cbEmpty(key === 'homework' ? 'Keine Hausaufgaben' : 'Keine Klassenbucheinträge');

    /* Wischen nach links legt den Löschknopf frei — antippen öffnet den
       Eintrag zum Bearbeiten. */
    $$('#dsubBody .dsub__item').forEach(el => {
      el.addEventListener('click', e => {
        if (e.target.closest('[data-del]')) return;
        openItemSheet(key, +el.dataset.i);
      });
    });
    $$('#dsubBody [data-del]').forEach(b => b.addEventListener('click', async e => {
      e.stopPropagation();
      try {
        const r = await cbPost(c, { action:'delItem', field:key, index:b.dataset.del });
        cbCache[key] = r.data; store.set(cbLocalKey(c, key), r.data);
      } catch (e2){ toast('Nicht gespeichert: ' + e2.message); }
      openClassbook(key);
    }));
  }

  if (CB[key].kind === 'students') renderAttendance(c);
  dsubSucheAnwenden();

  $('#detailSub').hidden = false;
}

/* Klassenbucheinträge filtern: Einträge, die den Suchtext nicht enthalten,
   treten aus der Liste. */
function dsubSucheAnwenden(){
  const box = $('#dsubSucheBox'), feld = $('#dsubSuche');
  if (!box || !feld) return;
  const q = box.hidden ? '' : feld.value.trim().toLowerCase();
  $$('#dsubBody .dsub__item').forEach(el => {
    el.hidden = !!q && !el.textContent.toLowerCase().includes(q);
  });
}
if ($('#dsubSuche')) $('#dsubSuche').addEventListener('input', dsubSucheAnwenden);

/* ---------------------------------------------------------------------
   Anwesenheitskontrolle
   --------------------------------------------------------------------- */
let attList = [], attFilter = 'all';
/* Mehrfachauswahl für einen gemeinsamen Klassenbucheintrag (Buch-Symbol). */
let attPick = new Set();
const ATT_BESEN = '<svg viewBox="0 0 24 24" class="ico"><path d="M19.6 4.4 13 11"/><path d="M11.3 9.4l3.3 3.3-1 1.1c-.9.9-1.3 2.3-1 3.6l.4 1.7c-2.9-.3-5.6-1.6-7.6-3.7l1.7-.4c1.3-.3 2.4-1.1 3-2.3z"/><path d="M7.9 15.7 6.6 17M10.3 17.4l-.9 1.5"/></svg>';
/* Erst wenn wirklich kontrolliert wurde, steht das grüne Häkchen da.
   Beides gehört zu genau einer Stunde — welcher, hält attFor fest. Ohne
   das zeigte die Übersicht die Werte der zuletzt geöffneten Stunde. */
let attChecked = false, attAbsent = 0, attFor = null;

const attKey = c => c ? `${c.date}#${c.lesson}${c._teil ? 'e' : ''}#${c.class || c.classOrig || ''}` : null;

/* „hart" wirft auch dann alles weg, wenn dieselbe Stunde noch einmal
   geöffnet wird. Sonst bliebe eine Liste stehen, die inzwischen am
   Desktop geändert worden sein kann — und die Zahl der Fehlenden käme
   weiter aus dem alten Stand statt aus der frischen Abfrage. */
function attReset(c, hart){
  const k = attKey(c);
  if (attFor === k && !hart) return;
  attFor = k; attList = []; attChecked = false; attAbsent = 0; attFilter = 'all'; attPick = new Set();
  /* Sonst bliebe der Hinweis der zuletzt geöffneten Stunde stehen. */
  attWegKlassen = []; cbFaellig = [];
}

/* Nur die Kurzfassung holen — die volle Liste erst, wenn man sie öffnet. */
/* Stiller Abgleich im Hintergrund: ändert sich etwas, wird neu gezeichnet. */
async function attRefresh(c){
  const k = attKey(c);
  try {
    const r = await fetch(CB_URL + '?' + cbParams(c, { action:'students' }), { credentials:'same-origin' });
    const d = await r.json();
    if (!d || !d.ok || attKey(state.detail) !== k) return;
    const neu = d.students || [];
    if (JSON.stringify(neu) === JSON.stringify(attList)) return;
    attList = neu;
    attChecked = !!d.checked;
    attAbsent  = attList.filter(s => s.state === 'absent').length;
    if (cbCurrent === 'students') drawAttendance(c);
    renderDetail();
  } catch (e){ /* offline — der geladene Stand bleibt stehen */ }
}

async function attLoadSummary(c){
  const k = attKey(c);
  try {
    const r = await fetch(CB_URL + '?' + cbParams(c, { action:'attSummary' }), { credentials:'same-origin' });
    const d = await r.json();
    if (!d || !d.ok || attKey(state.detail) !== k) return;
    attChecked = !!d.checked;
    attAbsent  = +d.absent || 0;
    if (state.detail) renderDetail();
  } catch (e){ /* offline — dann bleibt es beim Stand von vorhin */ }
}

/* Wie viele der Klasse fehlen — steht als roter Zähler in den Details. */
/* Ist die Liste geladen, zählt sie — sie ist der genauere Stand. Sonst
   die Zahl aus der Kurzabfrage. */
const attAbsentCount = () =>
  attList.length
    ? attList.filter(s => s.state === 'absent').length
    : attAbsent;

async function renderAttendance(c){
  attReset(c);
  const body = $('#dsubBody');

  /* Beim Öffnen der Stundendetails wurde schon alles geholt — dann steht
     die Liste sofort da und wird nur noch still aufgefrischt. */
  if (attList.length){
    drawAttendance(c);
    absLoadReasons(c).then(() => { if (state.detail === c && cbCurrent === 'students') drawAttendance(c); });
    attRefresh(c);
    return;
  }

  body.innerHTML = '<div class="dsub__empty"><span>Wird geladen …</span></div>';
  try {
    const res = await fetch(CB_URL + '?' + cbParams(c, { action:'students' }), { credentials:'same-origin' });
    const data = await res.json();
    attList = (data && data.students) || [];
    attChecked = !!(data && data.checked);
    attAbsent  = attList.filter(s => s.state === 'absent').length;
    if (!attList.length){
      body.innerHTML = `<div class="dsub__empty">
        <b>Keine Schüler*innen</b><span>${esc((data && data.hint) || 'Derzeit gibt es hier noch nichts')}</span></div>`;
      return;
    }
  } catch (e){
    body.innerHTML = '<div class="dsub__empty"><b>Nicht erreichbar</b><span>Die Liste braucht eine Verbindung</span></div>';
    return;
  }
  /* Die Gründe im Hintergrund holen — dann steht in der Liste der Name
     statt des Kürzels. */
  absLoadReasons(c).then(() => { if (state.detail === c && cbCurrent === 'students') drawAttendance(c); });
  drawAttendance(c);
}

const ATT_LABEL = { absent:'Abwesend', late:'Verspätet', present:'', open:'' };

/* In der Datenbank steht meist das Kürzel („BE"). In der Liste soll aber
   der ausgeschriebene Grund stehen. */
function attReasonName(code){
  if (!code) return '';
  const list = absReasons || ABS_FALLBACK;
  const hit = list.find(r => (r.code || r.name) === code || r.name === code);
  return hit ? hit.name : code;
}

/* Eingetragen wird frühestens eine Viertelstunde vor Stundenbeginn — vorher
   steht die Klasse ja noch gar nicht vor der Tür. */
const ATT_LEAD = 15;

function attWindow(c){
  const now = new Date(), today = iso(now);
  const t1  = (state.times[c.lesson] || {}).start || '';
  const t2  = (state.times[Math.min(c.lesson + c.span - 1, 12)] || {}).end || '';
  /* „läuft" heißt: die Stunde ist noch nicht vorbei. Nur dann ergibt eine
     Verspätung überhaupt Sinn. */
  if (c.date < today) return { open:true,  läuft:false, from:t1 };
  if (c.date > today) return { open:false, läuft:false, from:t1 };
  if (!t1)            return { open:true,  läuft:true,  from:t1 };

  const [h, m] = t1.split(':').map(Number);
  const from = h * 60 + m - ATT_LEAD;
  const jetzt = now.getHours() * 60 + now.getMinutes();
  const bis = t2 ? (+t2.split(':')[0] * 60 + +t2.split(':')[1]) : (h * 60 + m + 45);
  const two = x => String(x).padStart(2, '0');
  return {
    open:  jetzt >= from,
    läuft: jetzt <= bis,
    from:  `${two(Math.floor(from / 60))}:${two(((from % 60) + 60) % 60)}`
  };
}

/* Wird jemand WÄHREND der laufenden Stunde auf anwesend gesetzt, zählt das
   als verspätet — mit Zeitstempel „gekommen um HH:MM". */
const attNowHM = () => {
  const n = new Date();
  return `${String(n.getHours()).padStart(2,'0')}:${String(n.getMinutes()).padStart(2,'0')}`;
};

function attArrival(c, want){
  if (want !== 'present') return { state:want, arrival:'' };
  const st  = (state.times[c.lesson] || {}).start;
  const now = new Date();
  const läuft = st && c.date === iso(now)
             && now.getHours() * 60 + now.getMinutes() > +st.split(':')[0] * 60 + +st.split(':')[1];

  /* „force" kommt vom Wischen auf jemanden, der als abwesend geführt wird —
     das ist immer eine Verspätung, auch wenn die Stunde längst vorbei ist.
     Dann gilt die aktuelle Uhrzeit, sonst der Stundenbeginn. */
  /* Ist die Stunde vorbei, wird niemand mehr „verspätet" — dann einfach
     anwesend, ohne Uhrzeit. */
  if (läuft && attWindow(c).läuft) return { state:'late', arrival:attNowHM() };
  return { state:'present', arrival:'' };
}

/* Symbole der Vorlage. Der Zustand steht links im Kreis: grüner Haken =
   anwesend, rote Uhr = abwesend, gelbe Rücklaufuhr = später gekommen. */
const ATT_ICON = {
  open:    '<circle cx="12" cy="12" r="9"/>',
  present: '<circle cx="12" cy="12" r="9"/><path d="m8.1 12.2 2.7 2.7 5.1-5.3"/>',
  absent:  '<circle cx="12" cy="12" r="9"/><path d="M12 7.2V12l3.2 2"/>',
  late:    '<path d="M5.04 7.3A8.4 8.4 0 1 1 5.12 16.82"/>'
         + '<path d="M4.2 6.5 3.2 11.3l4.8-.9"/>'
         + '<path d="M12 7.9V12h3.9"/>'
};

/* Rechts in der Zeile: Vermerk (vorhanden = Buch, sonst Seite mit Plus)
   und die Uhr, die das Fenster zur Abwesenheit öffnet. */
const ATT_ACT_ICON = {
  noteAdd: '<path d="M18.6 11.8V5.9A1.5 1.5 0 0 0 17.1 4.4H6.3A1.5 1.5 0 0 0 4.8 5.9v11.9a1.5 1.5 0 0 0 1.5 1.5h5.3"/>'
         + '<circle cx="17.3" cy="17.3" r="4.2"/><path d="M17.3 14.9v4.8M14.9 17.3h4.8"/>',
  note:    '<path d="M6.4 4.5h11a1.5 1.5 0 0 1 1.5 1.5v13.5H7.9a1.5 1.5 0 0 1-1.5-1.5z"/>'
         + '<path d="M6.4 17.6h12.5"/>',
  clock:   '<circle cx="12" cy="12" r="8.6"/><path d="M12 7.2V12l3.1 2"/>'
};

/* Das Band über der Liste. Das Häkchen erscheint erst, wenn die Kontrolle
   auch wirklich gemacht wurde — vorher steht dort nur, ab wann es losgeht. */
function attBand(c, win, d, two){
  if (!win.open)
    return `<div class="att__band">Kontrolle der Abwesenheiten ist ab `
         + `${two(d.getDate())}.${two(d.getMonth()+1)}.${d.getFullYear()} – ${win.from} möglich</div>`;
  if (attChecked)
    return '<div class="att__band"><svg viewBox="0 0 24 24" class="att__band-ok">'
         + '<path d="m5 12.5 4.5 4.5L19 7"/></svg> Abwesenheiten wurden kontrolliert</div>';
  return '<div class="att__band att__band--todo">Abwesenheiten noch nicht kontrolliert</div>';
}

function drawAttendance(c){
  const n = s => attList.filter(x => x.state === s).length;
  /* Verspätete sind anwesend — sie erhöhen die Fehlzahl nicht. */
  const nAbs = n('absent');
  const shown = attList.filter(s =>
    attFilter === 'all' ? true
    : attFilter === 'absent' ? s.state === 'absent'
    : s.state === 'present');

  const d = parseISO(c.date);
  const two = x => String(x).padStart(2, '0');
  const t1 = (state.times[c.lesson] || {}).start || '';

  const win = attWindow(c);

  /* Bei einer Kopplung sitzen zwei Klassen gemeinsam im Unterricht und
     stehen deshalb in einer Liste. Sortiert wird dann nach Klasse, nicht
     nach Anfangsbuchstaben: Zwei ineinander verschränkte Klassen sind
     beim Aufrufen nicht zu handhaben — man sucht ein Kind in seiner
     Gruppe. Die Zwischenüberschrift trägt entsprechend den Klassennamen. */
  const klassen = [...new Set(shown.map(s => String(s.class || '')).filter(Boolean))];
  const nachKlasse = klassen.length > 1;

  const letterOf = s => (s.name.trim()[0] || '#').toUpperCase();
  let lastL = '';
  const rowsHtml = shown.map(s => {
    const L = nachKlasse ? String(s.class || '') : letterOf(s);
    const head = L !== lastL
      ? (lastL = L, `<div class="att__letter ${nachKlasse ? 'att__letter--class' : ''}"
                          id="att-l-${esc(L)}">${esc(L)}</div>`)
      : '';
    /* Der Grund steht in Klammern, der Entschuldigungsstand als Wort —
       er kommt aus dem Status, nicht aus dem Grund. */
    const grund = s.reason ? ' (' + esc(attReasonName(s.reason)) + ')' : '';
    const tag = s.gone
      ? `gegangen um ${esc(s.gone)}${grund}`
      : s.arrival
        ? `gekommen um ${esc(s.arrival)}${grund}`
        : (ATT_LABEL[s.state] ? `${ATT_LABEL[s.state]}${grund || (s.state === 'absent' ? ' (fehlt)' : '')}` : '');
    /* Nur „entschuldigt" steht da. Fehlt der Hinweis, ist es eben nicht
       entschuldigt — das muss man nicht auch noch hinschreiben. */
    const stand = (s.state === 'absent' && !s.locked && s.excused) ? 'entschuldigt' : '';
    return head + `
      <div class="att__row is-${s.state} ${s.locked ? 'is-locked' : ''} ${attPick.has(s.id) ? 'is-picked' : ''}" data-id="${s.id}">
        <span class="att__swipe att__swipe--in ${attRight(s.state, win.läuft) === 'late' ? 'is-late' : ''}" aria-hidden="true">
          <svg viewBox="0 0 24 24">${attRight(s.state, win.läuft) === 'late' ? ATT_ICON.late : ATT_ICON.present}</svg>
        </span>
        <span class="att__swipe att__swipe--out" aria-hidden="true">
          <svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/><path d="M12 7.2V12l3.2 2"/></svg>
        </span>
        <div class="att__inner">
          <button class="att__mark" data-act="cycle"
                  aria-label="${s.locked ? 'Entschuldigt — nicht änderbar' : 'Anwesenheit ändern'}">
            <svg viewBox="0 0 24 24">${ATT_ICON[s.state] || ATT_ICON.open}</svg>
          </button>
          <span class="att__main">
            <b>${esc(s.name)}</b>
            ${tag ? `<span class="att__tag">${tag}</span>` : ''}
            ${(s.duties || []).length ? `<span class="att__duty">${
                esc(s.duties.join(' · '))}</span>` : ''}
            ${s.source ? `<span class="att__before">${esc(s.source)}</span>` : ''}
            ${s.seit ? `<span class="att__before att__before--warn">War in Vorstunde abwesend!</span>` : ''}
            ${stand ? `<span class="att__state att__state--ok">${stand}</span>` : ''}
            ${(s.remarks || []).map((r, ri) => `<span class="att__note" data-remark="${ri}">${
                esc((r.type && r.type !== 'custom' ? r.type + ': ' : '') + r.text)}</span>`).join('')}
            ${s.note ? `<span class="att__note">${esc(s.note)}</span>` : ''}
          </span>
          <span class="att__acts">
            <button data-act="note" class="${attPick.has(s.id) ? 'is-on' : ''}" aria-pressed="${attPick.has(s.id)}"
                    aria-label="Für Klassenbucheintrag auswählen">
              <svg viewBox="0 0 24 24">${(s.remarks || []).length ? ATT_ACT_ICON.note : ATT_ACT_ICON.noteAdd}</svg>
            </button>
            <button data-act="absence" aria-label="Abwesenheit bearbeiten">
              <svg viewBox="0 0 24 24">${ATT_ACT_ICON.clock}</svg>
            </button>
          </span>
        </div>
      </div>`;
  }).join('');

  const letters = [...new Set(shown.map(letterOf))];

  const oldIdx = document.getElementById('attIndex'); if (oldIdx) oldIdx.remove();

  $('#dsubBody').innerHTML = `
    <div class="dsub__seg" id="attSeg">
      <button data-f="all"     class="${attFilter === 'all' ? 'is-active' : ''}">Alle (${attList.length})</button>
      <button data-f="absent"  class="${attFilter === 'absent' ? 'is-active' : ''}">Abwesend (${nAbs})</button>
      <button data-f="present" class="${attFilter === 'present' ? 'is-active' : ''}">Anwesend (${n('present')})</button>
    </div>
    ${attBand(c, win, d, two)}
    ${attWegKlassen.length ? `<div class="att__gone">${
        esc(attWegKlassen.join(', '))} ${attWegKlassen.length > 1 ? 'sind' : 'ist'} abwesend</div>` : ''}
    <div class="att ${win.open ? '' : 'is-locked'}" id="attList">${rowsHtml}</div>
    ${attPick.size ? `
      <div class="att__bar">
        <button type="button" class="att__bar-x" data-bar="cancel" aria-label="Auswahl aufheben">
          <svg viewBox="0 0 24 24"><path d="m7 7 10 10M17 7 7 17"/></svg></button>
        <button type="button" class="att__bar-go" data-bar="entry">Klassenbucheintrag (${attPick.size})</button>
      </div>`
    : (!state.isStudent && !store.get('att.hinweisZu', false)) ? `
      <div class="att__hint" role="note">
        <span class="att__hint-ico" aria-hidden="true">${DICO.classbook}</span>
        <span class="att__hint-text"><b>Klassenbucheintrag</b>
          <span>Tippen Sie mehrere Schüler*innen an, um einen gemeinsamen Eintrag zu erstellen.</span></span>
        <button type="button" class="att__hint-x" aria-label="Hinweis schließen">
          <svg viewBox="0 0 24 24"><path d="m8 8 8 8M16 8l-8 8"/></svg></button>
      </div>` : ''}`;

  /* Die Buchstabenleiste passt nur zu einer nach Namen sortierten Liste.
     Bei einer Kopplung stehen dort Klassennamen als Überschrift, und ein
     Sprung auf „M" liefe ins Leere. */
  if (!nachKlasse && letters.length > 1){
    const idx = document.createElement('div');
    idx.className = 'att__index'; idx.id = 'attIndex';
    idx.innerHTML = letters.map(L => `<button data-l="${L}">${L}</button>`).join('');
    $('#detailSub').appendChild(idx);
    $$('#attIndex button').forEach(b => b.addEventListener('click', () => {
      const t = document.getElementById('att-l-' + b.dataset.l);
      const body = $('#dsubBody');
      if (t && body){
        const delta = t.getBoundingClientRect().top - body.getBoundingClientRect().top;
        body.scrollTo({ top: Math.max(0, body.scrollTop + delta - 6), behavior:'smooth' });
      }
    }));
  }

  $$('#attSeg button').forEach(b => b.addEventListener('click', () => {
    attFilter = b.dataset.f; drawAttendance(c);
  }));

  /* Hinweis „Klassenbucheintrag“ schließen – bleibt danach weg. */
  const hx = $('#dsubBody .att__hint-x');
  if (hx) hx.addEventListener('click', () => { store.set('att.hinweisZu', true); drawAttendance(c); });
  $$('#dsubBody [data-bar]').forEach(b => b.addEventListener('click', () => {
    if (b.dataset.bar === 'cancel'){ attPick = new Set(); drawAttendance(c); return; }
    gemeinsamerEintrag(c);
  }));
  /* Eine vorhandene Bemerkung antippen öffnet sie zum Bearbeiten. */
  $$('#dsubBody .att__note[data-remark]').forEach(n => n.addEventListener('click', e => {
    e.stopPropagation();
    if (state.isStudent) return;
    const rec = attList.find(x => x.id === +n.closest('.att__row').dataset.id);
    if (rec) openNoteSheet(c, rec, (rec.remarks || [])[+n.dataset.remark] || null);
  }));

  $$('#dsubBody .att__row button').forEach(b => b.addEventListener('click', async () => {
    const rec = attList.find(x => x.id === +b.closest('.att__row').dataset.id);
    const act = b.dataset.act;

    if (state.isStudent) return;          /* kein Eintragungsrecht */

    /* Die Uhr öffnet das eigene Fenster für die Abwesenheit — dort stehen
       Zeitraum, Grund, Text und die Benachrichtigung.

       Ansehen darf man das immer, auch bei einer entschuldigten Stunde und
       auch vor dem Eintragungsfenster. Nur geändert wird dort dann nichts:
       Das Fenster steht in diesem Fall nur zum Lesen offen. Vorher fing
       die Sperre schon den Aufruf ab, und gerade die Klassenleitung kam an
       die Angaben zu einer entschuldigten Abwesenheit nicht mehr heran. */
    if (act === 'absence'){ openAbsList(c, rec); return; }

    if (rec && rec.locked){
      toast('Entschuldigt — geändert wird das dort, wo die Entschuldigung gepflegt ist');
      return;
    }
    if (!win.open){
      toast(`Eintragen ist ab ${win.from} möglich`);
      return;
    }

    if (act === 'note'){
      /* Das Buch wählt die Schülerin bzw. den Schüler für einen gemeinsamen
         Klassenbucheintrag aus (wie im Sollbild); unten erscheint dann die
         Leiste zum Anlegen. */
      if (attPick.has(rec.id)) attPick.delete(rec.id); else attPick.add(rec.id);
      drawAttendance(c);
      return;
    }

    {
      /* Reihum: offen → anwesend → abwesend → offen. „Anwesend" während der
         laufenden Stunde zählt als verspätet mit Zeitstempel. */
      const next = rec.state === 'open' ? 'present'
                 : rec.state === 'present' || rec.state === 'late' ? 'absent'
                 : rec.state === 'absent' ? 'open' : 'present';
      const r = attArrival(c, next);
      rec.state = r.state; rec.arrival = r.arrival;
      if (r.state !== 'absent') rec.reason = '';
    }
    attChecked = true;
    drawAttendance(c);
    if (state.detail === c) renderDetail();
    try { await cbPost(c, { action:'attendance', student_id:String(rec.id),
                            state:rec.state, note:rec.note || '', arrival:rec.arrival || '',
                            reason:rec.reason || '' }); }
    catch (e){ toast('Nicht gespeichert: ' + e.message); }
  }));

  attBindSwipe(c);
}

/* ---------------------------------------------------------------------
   Wisch-Geste wie in der Vorlage: die Zeile wandert mit dem Finger, aus
   der frei werdenden Kante wächst eine farbige Pille — links grün mit
   Haken (anwesend), rechts rot mit Uhr (abwesend). Über die Schwelle
   hinaus wird der Zustand gesetzt.
   --------------------------------------------------------------------- */
const ATT_SWIPE_MAX = 132;   /* so weit lässt sich die Zeile ziehen */
const ATT_SWIPE_HIT = 62;    /* ab hier greift die Aktion */
const ATT_SWIPE_START = 8;   /* ab hier gilt die Bewegung als Geste */
const ATT_SWIPE_GAP = 12;    /* Luft zwischen Knopf und Zeile */

/* Was das Wischen je Zustand bedeutet:
   nach rechts — wer schon anwesend ist, ist es bereits; wer als abwesend
   geführt wird, kommt verspätet dazu (gelbe Uhr).
   nach links  — wer schon abwesend ist, ist es bereits.
   „null" heißt: gesperrt, die Zeile federt zurück. */
const attRight = (st, läuft) => (st === 'present' || st === 'late') ? null
                              : (st === 'absent' ? (läuft ? 'late' : 'present') : 'present');
const attLeft  = st => st === 'absent' ? null : 'absent';

function attBindSwipe(c){
  const win = attWindow(c);
  const offen = win.open;
  $$('#dsubBody .att__row').forEach(row => {
    const rec   = attList.find(x => x.id === +row.dataset.id);
    const inner = row.querySelector('.att__inner');
    const pIn   = row.querySelector('.att__swipe--in');
    const pOut  = row.querySelector('.att__swipe--out');
    let sx = 0, sy = 0, sdx = 0, slock = null, sdrag = false, pid = null, schlucken = false;

    /* Gesperrte Richtungen lassen sich gar nicht erst aufziehen. */
    const erlaubt = dx => state.isStudent ? false
                        : (rec && rec.locked) ? false
                        : dx > 0 ? !!attRight(rec && rec.state, win.läuft)
                        : dx < 0 ? !!attLeft(rec && rec.state) : true;

    /* Die Zeile wandert mit dem Finger; in der frei werdenden Fläche steht
       der Knopf wie im Original – eine Pille mit etwas Luft zur Zeile. */
    const paint = dx => {
      if (!erlaubt(dx)) dx = 0;
      inner.style.transform = `translateX(${dx}px)`;
      const w = d => Math.max(0, Math.abs(d) - ATT_SWIPE_GAP);
      pIn .style.width = (dx > 0 ? w(dx) : 0) + 'px';
      pOut.style.width = (dx < 0 ? w(dx) : 0) + 'px';
      row.classList.toggle('is-armed', Math.abs(dx) >= ATT_SWIPE_HIT);
    };
    const reset = () => {
      row.classList.remove('is-swiping', 'is-armed');
      inner.style.transform = '';
      pIn.style.width = pOut.style.width = '';
    };

    /* Zeigerereignisse: Finger, Stift und Maus nehmen denselben Weg. Die
       Zeile lässt senkrechtes Rollen zu (touch-action: pan-y) – erst eine
       waagerechte Bewegung über die Schwelle wird zur Geste. */
    row.addEventListener('pointerdown', e => {
      if (!e.isPrimary || (e.pointerType === 'mouse' && e.button !== 0)) return;
      if (!offen || (rec && rec.locked) || state.isStudent) return;
      sx = e.clientX; sy = e.clientY; sdx = 0; slock = null; sdrag = true; pid = e.pointerId;
    });

    row.addEventListener('pointermove', e => {
      if (!sdrag || e.pointerId !== pid) return;
      const mx = e.clientX - sx, my = e.clientY - sy;
      if (slock === null){
        if (Math.abs(mx) < ATT_SWIPE_START && Math.abs(my) < ATT_SWIPE_START) return;
        slock = Math.abs(mx) > Math.abs(my) * 1.3 ? 'x' : 'y';
        if (slock === 'x'){ try { row.setPointerCapture(pid); } catch (err){} }
      }
      if (slock !== 'x') return;
      e.preventDefault();
      sdx = Math.max(-ATT_SWIPE_MAX, Math.min(ATT_SWIPE_MAX, mx));
      row.classList.add('is-swiping');
      paint(sdx);
    });

    const end = async e => {
      if (!sdrag || (e && e.pointerId !== undefined && e.pointerId !== pid)) return;
      sdrag = false;
      try { if (pid !== null) row.releasePointerCapture(pid); } catch (err){}
      pid = null;
      reset();
      if (slock !== 'x' || !rec) return;
      /* Der Klick, der auf eine Wischgeste folgt, gehört nicht zum Knopf
         darunter – sonst schaltete das Loslassen den Zustand gleich weiter. */
      schlucken = true; setTimeout(() => { schlucken = false; }, 350);
      if (e && e.type === 'pointercancel') return;
      const ziel = sdx >= ATT_SWIPE_HIT ? attRight(rec.state, win.läuft)
                 : sdx <= -ATT_SWIPE_HIT ? attLeft(rec.state) : null;
      if (!ziel) return;

      /* „late" heißt: anwesend, aber zu spät — mit Uhrzeit. */
      const r = ziel === 'late'   ? { state:'late', arrival:attNowHM() }
              : ziel === 'absent' ? { state:'absent', arrival:'' }
              : { state:'present', arrival:'' };
      if (rec.state === r.state && (rec.arrival || '') === r.arrival) return;
      rec.state = r.state; rec.arrival = r.arrival; rec.gone = '';
      if (r.state !== 'absent') rec.reason = '';
      if (r.state === 'absent') rec.seit = false;
      attChecked = true;
      buzz(); drawAttendance(c);
      if (state.detail === c) renderDetail();
      try { await cbPost(c, { action:'attendance', student_id:String(rec.id),
                              state:r.state, note:rec.note || '', arrival:r.arrival,
                              reason:rec.reason || '' }); }
      catch (err){ toast('Nicht gespeichert: ' + err.message); }
    };
    row.addEventListener('pointerup', end);
    row.addEventListener('pointercancel', end);
    row.addEventListener('click', e => {
      if (schlucken){ e.stopPropagation(); e.preventDefault(); }
    }, true);

    /* Zum Prüfen ohne Finger: dieselbe Geste von außen auslösen. */
    row.__swipe = { paint, reset, commit: end,
                    set dx(v){ sdx = v; slock = 'x'; sdrag = true; pid = null; } };
  });
}

/* =====================================================================
   Eingabefenster
   ---------------------------------------------------------------------
   Ein Fenster für alles, was eingetippt wird: neue Hausaufgabe, neuer
   Klassenbucheintrag, Bemerkung zu einer Schülerin, Notizfelder. Aufbau
   nach der Vorlage — Kreuz links, Titel, oranger Haken rechts, darunter
   beschriftete Zeilen.
   ===================================================================== */
let formSpec = null;

/* Listeneinträge werden als JSON abgelegt, damit Fälligkeit und Häkchen
   erhalten bleiben. Ältere Einträge sind reiner Text — die kommen ohne
   Umweg durch. */
function itemParse(t){
  if (typeof t === 'object' && t) return t;
  try {
    const o = JSON.parse(t);
    if (o && typeof o === 'object' && !Array.isArray(o)) return o;
  } catch (e){}
  return { text:String(t ?? '') };
}
const itemText = t => itemParse(t).text || '';

function openForm(spec){
  formSpec = spec;
  $('#dformTitle').textContent = spec.title;
  /* Erst sichtbar machen, dann zeichnen — sonst haben die mitwachsenden
     Textfelder keine messbare Höhe und bleiben auf null stehen. */
  $('#detailForm').hidden = false;
  drawForm();
  setTimeout(() => {
    const first = $('#dformBody textarea, #dformBody input[type="text"]');
    if (first && spec.focus !== false) first.focus();
  }, 140);
}

function closeForm(){
  $('#dformPick').hidden = true;
  $('#detailForm').hidden = true;
  formSpec = null;
}

/* Was im Fenster steht, zurück in die Felder schreiben. */
function formCollect(){
  if (!formSpec) return;
  formSpec.fields.forEach(f => {
    const el = document.getElementById('ff-' + f.key);
    if (!el) return;
    if (f.t === 'toggle') return;                 /* wird beim Tippen gesetzt */
    f.value = el.value;
  });
}

function drawForm(){
  if (!formSpec) return;
  const f = formSpec.fields;
  $('#dformBody').innerHTML = f.map(x => {
    if (x.t === 'hint')
      return `<p class="dform__hint">
                <svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/><path d="M12 11v5.4M12 7.9v.1"/></svg>
                ${esc(x.text)}</p>`;
    if (x.t === 'people')
      return `<button class="dform__row dform__row--nav dform__row--people" type="button" data-people>
                <span class="dform__lab">${esc(x.label)}<small>${esc(x.text || '')}</small></span>
                <span></span>
                <svg viewBox="0 0 24 24" class="dform__chev"><path d="m10 6 6 6-6 6"/></svg>
              </button>`;
    if (x.t === 'text')
      return `<label class="dform__row dform__row--nav">
                <span class="dform__lab">${esc(x.label)}</span>
                <textarea class="dform__in" id="ff-${x.key}" rows="1"
                  placeholder="${esc(x.placeholder || 'Text hier eingeben')}">${esc(x.value || '')}</textarea>
                <svg viewBox="0 0 24 24" class="dform__chev"><path d="m10 6 6 6-6 6"/></svg>
              </label>`;
    if (x.t === 'area')
      return `<div class="dform__block">
                <textarea class="dform__area" id="ff-${x.key}"
                  placeholder="${esc(x.label)}">${esc(x.value || '')}</textarea>
              </div>`;
    if (x.t === 'date')
      return `<label class="dform__row dform__row--val">
                <span class="dform__lab dform__lab--big">${esc(x.label)}</span>
                <input class="dform__date" type="date" id="ff-${x.key}" value="${esc(x.value || '')}">
              </label>`;
    if (x.t === 'pick')
      return `<button class="dform__row dform__row--nav" type="button" data-pick="${x.key}">
                <span class="dform__lab">${esc(x.label)}</span>
                ${x.value ? `<span class="dform__val">${esc(x.value)}</span>` : '<span></span>'}
                <svg viewBox="0 0 24 24" class="dform__chev"><path d="m10 6 6 6-6 6"/></svg>
              </button>`;
    if (x.t === 'toggle')
      return `<div class="dform__row dform__row--val">
                <span class="dform__lab dform__lab--big">${esc(x.label)}</span>
                <span class="dtoggle ${x.value ? 'is-on' : ''}" data-toggle="${x.key}"
                      role="switch" aria-checked="${x.value ? 'true' : 'false'}"></span>
              </div>`;
    return '';
  }).join('');

  /* Textfelder wachsen mit dem Inhalt — kein Rollbalken im Fenster. */
  $$('#dformBody textarea').forEach(t => {
    const grow = () => {
      t.style.height = 'auto';
      t.style.height = Math.max(24, t.scrollHeight) + 'px';
    };
    t.addEventListener('input', grow);
    grow();
    requestAnimationFrame(grow);          /* nach dem ersten Bild nachmessen */
  });

  $$('#dformBody [data-toggle]').forEach(b => b.addEventListener('click', () => {
    formCollect();
    const x = f.find(y => y.key === b.dataset.toggle);
    x.value = !x.value;
    drawForm();
  }));

  /* „Schüler*innen (n)“ führt zurück zur Liste, um die Auswahl zu ändern. */
  $$('#dformBody [data-people]').forEach(b => b.addEventListener('click', () => closeForm()));

  $$('#dformBody [data-pick]').forEach(b => b.addEventListener('click', () => {
    formCollect();
    const x = f.find(y => y.key === b.dataset.pick);
    const pick = $('#dformPick');
    pick.innerHTML = (x.options || []).map((o, i) =>
      `<button type="button" data-n="${i}" class="${o === x.value ? 'is-on' : ''}">${esc(o)}</button>`).join('');
    const host = $('#detailForm').getBoundingClientRect();
    const r = b.getBoundingClientRect();
    pick.style.top = (r.bottom - host.top + 6) + 'px';
    pick.style.maxHeight = Math.max(150, host.bottom - r.bottom - 26) + 'px';
    pick.hidden = false;
    $$('#dformPick button').forEach(o => o.addEventListener('click', () => {
      x.value = x.options[+o.dataset.n];
      pick.hidden = true;
      drawForm();
    }));
  }));
}

$('#dformClose').addEventListener('click', closeForm);
$('#dformSave').addEventListener('click', async () => {
  if (!formSpec) return;
  formCollect();
  const spec = formSpec;
  const werte = {};
  spec.fields.forEach(x => { if (x.key) werte[x.key] = x.t === 'toggle' ? !!x.value : (x.value || ''); });
  closeForm();
  await spec.onSave(werte);
});
document.addEventListener('click', e => {
  const pick = $('#dformPick');
  if (!pick || pick.hidden) return;
  if (!e.target.closest('#dformPick') && !e.target.closest('[data-pick]')) pick.hidden = true;
});

/* --- Bemerkung zu einer Schülerin / einem Schüler ---------------------
   Geschrieben wird in classbook_student_remarks — dieselbe Tabelle wie am
   Desktop. Auch die Arten der Bemerkung kommen von dort. */
let remarkTypes = null;

async function loadRemarkTypes(c){
  if (remarkTypes) return remarkTypes;
  try {
    const r = await fetch(CB_URL + '?' + cbParams(c, { action:'remarkTypes' }), { credentials:'same-origin' });
    const d = await r.json();
    remarkTypes = (d && d.types) || [];
  } catch (e){ remarkTypes = []; }
  return remarkTypes;
}

/* Die Antwort bringt alle Bemerkungen der Stunde zurück. */
function applyRemarks(map){
  if (!map) return;
  attList.forEach(s => { s.remarks = map[String(s.id)] || []; });
}

async function openNoteSheet(c, rec, alt){
  const arten = await loadRemarkTypes(c);
  const namen = arten.map(t => t.name);
  const jetzt = alt ? (arten.find(t => t.code === alt.type) || null) : null;

  openForm({
    title: alt ? 'Bemerkung' : 'Neue Bemerkung',
    fields: [
      ...(namen.length
        ? [{ t:'pick', key:'art', label:'Art der Bemerkung',
             value:(jetzt ? jetzt.name : ''), options:namen }]
        : []),
      { t:'text', key:'text', label:rec.name, value:(alt ? alt.text : ''),
        placeholder:'Text hier eingeben' }
    ],
    onSave: async v => {
      const text = (v.text || '').trim();
      if (!text){ toast('Ohne Text lässt sich nichts speichern'); return; }
      const art = (arten.find(t => t.name === v.art) || {}).code || '';
      try {
        const r = await cbPost(c, { action:'remarkSave', student_id:String(rec.id),
                                    id:String(alt ? alt.id : 0), type:art, text });
        applyRemarks(r.remarks);
      } catch (e){ toast('Nicht gespeichert: ' + e.message); }
      drawAttendance(c);
    }
  });
}

/* Gemeinsamer Klassenbucheintrag für alle ausgewählten Schüler*innen. */
async function gemeinsamerEintrag(c){
  const arten = await loadRemarkTypes(c);
  const namen = arten.map(t => t.name);
  const gewaehlt = attList.filter(s => attPick.has(s.id));
  /* Wie im Original: „Neuer Klassenbucheintrag“ mit den gewählten
     Schüler*innen, der Eintragskategorie und dem Text. */
  openForm({
    title: 'Neuer Klassenbucheintrag',
    focus: false,
    fields: [
      { t:'people', label:`Schüler*innen (${gewaehlt.length})`, text: gewaehlt.map(s => s.name).join(', ') },
      ...(namen.length ? [{ t:'pick', key:'art', label:'Eintragskategorie', value:'', options:namen }] : []),
      { t:'area', key:'text', label:'Hier klicken um Text einzutragen.', value:'' }
    ],
    onSave: async v => {
      const text = (v.text || '').trim();
      if (!text){ toast('Ohne Text lässt sich nichts speichern'); return; }
      const art = (arten.find(t => t.name === v.art) || {}).code || '';
      try {
        const r = await cbPost(c, { action:'remarkMany', student_ids:[...attPick].join(','), type:art, text });
        applyRemarks(r.remarks);
        toast(gewaehlt.length === 1 ? 'Eintrag gespeichert' : `Eintrag für ${gewaehlt.length} Schüler*innen gespeichert`);
        attPick = new Set();
      } catch (e){ toast('Nicht gespeichert: ' + e.message); }
      drawAttendance(c);
    }
  });
}

/* Besen: alle (nicht entschuldigten) Schüler*innen als anwesend markieren. */
async function alleAnwesend(c){
  const weg = attList.filter(s => !s.locked && (s.state === 'absent' || s.state === 'late'));
  if (weg.length && !confirm(`${weg.length === 1 ? '1 Schüler*in' : weg.length + ' Schüler*innen'} als anwesend markieren?`)) return;
  weg.forEach(s => { s.state = 'present'; s.arrival = ''; s.reason = ''; });
  attChecked = true;
  buzz(); drawAttendance(c); renderDetail();
  try {
    for (const s of weg)
      await cbPost(c, { action:'attendance', student_id:String(s.id), state:'present', note:s.note || '', arrival:'', reason:'' });
    if (!weg.length) await cbPost(c, { action:'setChecked' });
  } catch (e){ toast('Nicht gespeichert: ' + e.message); }
}

/* --- Hausaufgabe und Klassenbucheintrag ------------------------------- */
const CB_CATS = ['Bemerkung', 'Verspätung', 'Hausaufgaben fehlen', 'Material fehlt',
                 'Störung', 'Lob', 'Sonstiges'];
/* Die Kategorien sind die Vermerkarten von UntPlanWeb — geladen werden
   sie mit den Arten der Bemerkungen (dieselbe Liste). */
const cbKategorien = () => (remarkTypes && remarkTypes.length) ? remarkTypes.map(t => t.name) : CB_CATS;

async function openItemSheet(key, index){
  const c = state.detail;
  if (!c) return;
  if (key === 'classbook') await loadRemarkTypes(c);
  const list = Array.isArray(cbCache[key]) ? cbCache[key] : [];
  const alt  = index !== undefined ? itemParse(list[index]) : {};
  const neu  = index === undefined;

  /* Klassenbucheintrag wie im Original: Eintragskategorie, darunter der Text. */
  const fields = key === 'homework' ? [{ t:'text', key:'text', label:'Text', value:alt.text || '' }] : [];
  if (key === 'homework'){
    fields.push({ t:'area', key:'note', label:'Bemerkung', value:alt.note || '' });
    fields.push({ t:'hint', text:'Bemerkung ist nicht sichtbar für Schüler*innen und Erziehungsberechtigte' });
    fields.push({ t:'date', key:'due', label:'Fälligkeitsdatum', value:alt.due || iso(new Date()) });
  } else {
    fields.push({ t:'pick', key:'cat', label:'Eintragskategorie',
                  value:alt.cat || '', options:cbKategorien() });
    fields.push({ t:'area', key:'text', label:'Hier klicken um Text einzutragen.', value:alt.text || '' });
  }

  openForm({
    title: neu
      ? (key === 'homework' ? 'Neue Hausaufgabe' : 'Neuer Klassenbucheintrag')
      : (key === 'homework' ? 'Hausaufgabe' : 'Klassenbucheintrag'),
    fields,
    onSave: async v => {
      if (!(v.text || '').trim()){ toast('Ohne Text lässt sich nichts speichern'); return; }
      const obj = { text:v.text.trim() };
      if (key === 'homework'){
        if (v.note) obj.note = v.note.trim();
        if (v.due)  obj.due  = v.due;
      } else if (v.cat) obj.cat = v.cat;
      if (neu) obj.at = new Date().toISOString().slice(0, 16);
      else if (alt.at) obj.at = alt.at;

      try {
        const r = await cbPost(c, neu
          ? { action:'addItem', field:key, value:JSON.stringify(obj) }
          : { action:'setItem', field:key, index:String(index), value:JSON.stringify(obj) });
        cbCache[key] = r.data; store.set(cbLocalKey(c, key), r.data);
      } catch (e){ toast('Nicht gespeichert: ' + e.message); }
      openClassbook(key);
    }
  });
}

/* =====================================================================
   Abwesenheit bearbeiten
   ---------------------------------------------------------------------
   Ein eigenes Fenster über der Schülerliste: Zeitraum, Grund, Text und
   die Frage, ob die Erziehungsberechtigten bereits benachrichtigt sind.
   Es öffnet sich über das Uhr-Symbol in der Zeile.
   ===================================================================== */
let absCard = null, absRec = null, absDraft = null, absReasons = null;
/* Steht das Fenster nur zum Lesen offen? */
let absNurLesen = false;

/* Die Gründe kommen ausschließlich aus absence_reasons. Eine erfundene
   Ersatzliste wäre schädlich: ihre Kürzel kennt die Desktop-Version nicht,
   und dann stünde dort ein Grund, mit dem sie nichts anfangen kann. */
const ABS_FALLBACK = [{ code:'', name:'Ohne Angabe' }];

async function absLoadReasons(c){
  if (absReasons) return absReasons;
  try {
    const r = await fetch(CB_URL + '?' + cbParams(c, { action:'reasons' }), { credentials:'same-origin' });
    const d = await r.json();
    absReasons = (d && Array.isArray(d.reasons) && d.reasons.length)
      ? [{ code:'', name:'Ohne Angabe' }].concat(d.reasons)
      : ABS_FALLBACK.slice();
  } catch (e){ absReasons = null; }        /* später erneut versuchen */
  if (!absReasons) absReasons = ABS_FALLBACK.slice();
  return absReasons;
}

const absInitials = name => String(name || '').trim().split(/\s+/)
  .slice(0, 2).map(w => w[0] || '').join('').toUpperCase() || '–';

/* ---------------------------------------------------------------------
   Abwesenheiten einer Schülerin / eines Schülers (Uhr in der Zeile)
   ---------------------------------------------------------------------
   Wie im Original: Liste der Abwesenheiten mit Datum, Uhrzeit und Grund,
   oben rechts „+“ für eine neue, unten die Suche. Ein Eintrag öffnet
   „Abwesenheit bearbeiten“. Die Klassenleitung sieht alle Zeiträume der
   letzten Wochen, sonst steht dort die Abwesenheit dieser Stunde.
   --------------------------------------------------------------------- */
let ablCard = null, ablRec = null, ablItems = [], ablManage = false;

const hm = t => String(t || '').slice(0, 5).replace(/^0(\d)/, '$1');

async function openAbsList(c, rec){
  ablCard = c; ablRec = rec; ablItems = []; ablManage = false;
  $('#dablWho').textContent = rec.name;
  $('#dablSuche').value = '';
  $('#dablAdd').hidden = !!rec.locked || state.isStudent;
  $('#dablBody').innerHTML = '<div class="dsub__empty"><span>Wird geladen …</span></div>';
  $('#detailAbsList').hidden = false;
  await ablLaden();
}

async function ablLaden(){
  const c = ablCard, rec = ablRec;
  if (!c || !rec) return;
  absLoadReasons(c);
  try {
    const r = await fetch(CB_URL + '?' + cbParams(c, { action:'absences', student_id:String(rec.id) }), { credentials:'same-origin' });
    const d = await r.json();
    if (ablRec !== rec) return;
    if (!d || !d.ok) throw new Error((d && d.error) || 'Fehler');
    ablItems = d.items || []; ablManage = !!d.manage;
  } catch (e){
    /* Ohne Verbindung: was die Schülerliste schon weiß. */
    ablItems = rec.state === 'absent' || rec.state === 'late' ? [{
      fromDate: rec.from_date || c.date, fromTime: rec.from_time || '', toDate: rec.to_date || c.date, toTime: rec.to_time || rec.arrival || '',
      reason: rec.reason || '', reasonName: attReasonName(rec.reason), text: rec.note || '', current: true, ids: [] }] : [];
  }
  ablZeichnen();
}

function ablZeichnen(){
  const q = ($('#dablSuche').value || '').trim().toLowerCase();
  const liste = ablItems.filter(x => !q || JSON.stringify(x).toLowerCase().includes(q));
  const datum = x => x.toDate && x.toDate !== x.fromDate ? `${deDate(x.fromDate)} – ${deDate(x.toDate)}` : deDate(x.fromDate);
  $('#dablBody').innerHTML = liste.length ? liste.map(x => `
    <button type="button" class="dabl__item" data-i="${ablItems.indexOf(x)}">
      <svg viewBox="0 0 24 24" class="dabl__ico"><circle cx="12" cy="12" r="8.6"/><path d="M12 7.2V12l3.1 2"/></svg>
      <span class="dabl__when"><span>${esc(datum(x))}</span><span>${esc(hm(x.fromTime))} - ${esc(hm(x.toTime))}</span></span>
      <svg viewBox="0 0 24 24" class="ico dabl__chev"><path d="m9.5 5.5 6.5 6.5-6.5 6.5"/></svg>
      <svg viewBox="0 0 24 24" class="dabl__ico"><circle cx="12" cy="12" r="8.6"/><path d="M12 11.2v5.4"/><circle cx="12" cy="7.9" r=".95" fill="currentColor" stroke="none"/></svg>
      <span class="dabl__grund">${esc(x.reasonName || attReasonName(x.reason) || 'Ohne Angabe')}${x.text ? ` <i>· ${esc(x.text)}</i>` : ''}</span>
    </button>`).join('')
    : cbEmpty(q ? 'Keine Treffer' : 'Keine Abwesenheiten');
  $$('#dablBody .dabl__item').forEach(b => b.addEventListener('click', () => {
    openAbsence(ablCard, ablRec, ablItems[+b.dataset.i]);
  }));
}

function closeAbsList(){
  $('#detailAbsList').hidden = true;
  ablCard = null; ablRec = null; ablItems = [];
}

$('#dablBack').addEventListener('click', closeAbsList);
$('#dablAdd').addEventListener('click', () => { if (ablCard && ablRec) openAbsence(ablCard, ablRec, null); });
$('#dablSuche').addEventListener('input', ablZeichnen);

/* ---------------------------------------------------------------------
   Abwesenheit bearbeiten – minutengenau von–bis, Grund, Text,
   „Erziehungsberechtigte benachrichtigt“; Papierkorb löscht, Haken sichert.
   --------------------------------------------------------------------- */
let absItem = null;

function openAbsence(c, rec, item){
  absCard = c; absRec = rec; absItem = item || null;
  /* Nur lesen, wenn die Stunde entschuldigt ist oder das Eintragungsfenster
     noch nicht offen steht. */
  absNurLesen = !!(rec && rec.locked) || (!attWindow(c).open && !(item && ablManage));
  const t1 = ((state.times[c.lesson] || {}).start || '08:00').slice(0, 5);
  const t2 = ((state.times[Math.min(c.lesson + c.span - 1, 12)] || {}).end || t1).slice(0, 5);
  const z = v => v ? ('0' + String(v).slice(0, 5)).slice(-5) : '';

  absDraft = item ? {
    fromDate: item.fromDate || c.date, fromTime: z(item.fromTime) || t1,
    toDate:   item.toDate || item.fromDate || c.date, toTime: z(item.toTime) || t2,
    reason:   item.reason || '', text: item.text || '', notified: !!item.notified
  } : {
    /* Neu: die ganze Stunde; läuft sie gerade, endet die Fehlzeit nicht vor jetzt. */
    fromDate: c.date, fromTime: t1, toDate: c.date, toTime: t2,
    reason: '', text: '', notified: false
  };

  drawAbsence();
  $('#detailAbs').hidden = false;
  absLoadReasons(c).then(() => { if (absRec === rec) drawAbsence(); });
}

function closeAbsence(){
  $('#dabsPick').hidden = true;
  $('#detailAbs').hidden = true;
  absCard = null; absRec = null; absDraft = null; absItem = null;
}

/* Was im Fenster steht, in den Entwurf zurückschreiben. */
function absCollect(){
  if (!absDraft) return;
  const v = id => (document.getElementById(id) || {}).value || '';
  absDraft.fromDate = v('absFromDate') || absDraft.fromDate;
  absDraft.fromTime = v('absFromTime') || absDraft.fromTime;
  absDraft.toDate   = v('absToDate')   || absDraft.toDate;
  absDraft.toTime   = v('absToTime')   || absDraft.toTime;
  absDraft.text     = v('absText');
}

function drawAbsence(){
  const c = absCard, rec = absRec;
  if (!c || !rec) return;
  const d = absDraft;
  const klasse = rec.class || c.class || c.classOrig || '';
  const list = absReasons || ABS_FALLBACK;
  const hit = list.find(r => (r.code || r.name) === d.reason || r.name === d.reason);
  /* Steht ein Grund da, den die Liste nicht kennt, wird er trotzdem gezeigt. */
  const reasonLabel = hit ? hit.name : (d.reason || '');

  $('#dabsBody').innerHTML = `
    ${absNurLesen ? `<p class="dabs__lesen">${
      rec.locked ? 'Entschuldigt — geändert wird das dort, wo die Entschuldigung gepflegt ist.'
                 : 'Eintragen ist erst ab Stundenbeginn möglich.'}</p>` : ''}
    <div class="dabs__who">
      <span class="dabs__ava">${esc(absInitials(rec.name))}</span>
      <span class="dabs__id">
        <b>${esc(rec.name)}</b>
        ${klasse ? `<span class="dabs__chip">${esc(klasse)}</span>` : ''}
      </span>
    </div>

    <div class="dabs__grid">
      <div>
        <span class="dabs__lab">START</span>
        <div class="dabs__fields">
          <input class="dabs__pill" type="date" id="absFromDate" value="${esc(d.fromDate)}">
          <input class="dabs__pill" type="time" id="absFromTime" step="60" value="${esc(d.fromTime)}">
        </div>
      </div>
      <div>
        <span class="dabs__lab">ENDE</span>
        <div class="dabs__fields">
          <input class="dabs__pill" type="date" id="absToDate" value="${esc(d.toDate)}">
          <input class="dabs__pill" type="time" id="absToTime" step="60" value="${esc(d.toTime)}">
        </div>
      </div>
    </div>

    <span class="dabs__lab">ABWESENHEITSGRUND</span>
    <button class="dabs__sel" id="absReason" type="button">
      <span class="${reasonLabel ? '' : 'is-empty'}">${esc(reasonLabel || 'Grund wählen')}</span>
      <svg viewBox="0 0 24 24" class="ico"><path d="m10 6 6 6-6 6"/></svg>
    </button>

    <span class="dabs__lab">TEXT FÜR ABWESENHEIT</span>
    <textarea class="dabs__area" id="absText" placeholder="Text hier eingeben">${esc(d.text)}</textarea>

    <div class="dabs__switch">
      <span>Erziehungsberechtigte benachrichtigt</span>
      <span class="dtoggle ${d.notified ? 'is-on' : ''}" id="absNotified" role="switch"
            aria-checked="${d.notified ? 'true' : 'false'}"></span>
    </div>`;

  /* Nur lesen: Die Felder bleiben stehen, lassen sich aber nicht ändern. */
  if (absNurLesen){
    $$('#dabsBody input, #dabsBody textarea').forEach(el => { el.disabled = true; });
    $$('#dabsBody button').forEach(el => { el.disabled = true; });
    $('#dabsDelete').hidden = true; $('#dabsSave').hidden = true;
    return;
  }
  /* Löschen gibt es nur für eine bestehende Abwesenheit. */
  $('#dabsDelete').hidden = !absItem;
  $('#dabsSave').hidden = false;

  $('#absNotified').addEventListener('click', () => {
    absCollect();
    absDraft.notified = !absDraft.notified;
    drawAbsence();
  });

  $('#absReason').addEventListener('click', () => {
    absCollect();
    const pick = $('#dabsPick');
    pick.innerHTML = list.map((r, i) =>
      `<button type="button" data-n="${i}" class="${(r.code || r.name) === d.reason ? 'is-on' : ''}">
         ${esc(r.name)}
       </button>`).join('');
    /* Die Liste hängt direkt unter der Auswahlzeile. */
    const host = $('#detailAbs').getBoundingClientRect();
    const r = $('#absReason').getBoundingClientRect();
    pick.style.top = (r.bottom - host.top + 6) + 'px';
    pick.style.maxHeight = Math.max(150, host.bottom - r.bottom - 26) + 'px';
    pick.hidden = false;
    $$('#dabsPick button').forEach(b => b.addEventListener('click', () => {
      const r2 = list[+b.dataset.n];
      absDraft.reason = r2.code || '';
      pick.hidden = true;
      drawAbsence();
    }));
  });
}

/* Schreiben: Zeitraum von–bis mit Grund, Text und Benachrichtigung. Ob es
   eine Verspätung ist, ergibt sich aus den Zeiten: endet die Fehlzeit vor
   dem Stundenschluss und beginnt sie mit der Stunde, ist die Ankunft das Ende. */
async function absSchreiben(c, rec, d, item){
  if (!c || !rec || !d) return false;
  const t1 = ((state.times[c.lesson] || {}).start || '').slice(0, 5);
  const t2 = ((state.times[Math.min(c.lesson + c.span - 1, 12)] || {}).end || '').slice(0, 5);
  if (`${d.toDate} ${d.toTime}` <= `${d.fromDate} ${d.fromTime}`){
    toast('Das Ende muss nach dem Beginn liegen');
    return false;
  }
  /* Betrifft der Zeitraum diese Stunde, steht er sofort in der Liste. */
  const heute = d.fromDate <= c.date && d.toDate >= c.date
             && (d.fromDate < c.date || d.fromTime < t2) && (d.toDate > c.date || d.toTime > t1);
  const spaet = heute && d.fromDate === c.date && d.toDate === c.date && d.fromTime <= t1 && d.toTime < t2;
  const frueh = heute && d.fromDate === c.date && d.toDate === c.date && d.fromTime > t1 && d.toTime >= t2;
  if (heute){
    rec.state = spaet ? 'late' : 'absent';
    rec.arrival = spaet ? d.toTime : '';
    rec.gone = frueh ? d.fromTime : '';
    rec.reason = d.reason; rec.note = (d.text || '').trim();
    rec.from_date = d.fromDate; rec.from_time = d.fromTime;
    rec.to_date = d.toDate; rec.to_time = d.toTime;
    rec.notified = d.notified; rec.seit = false;
    attChecked = true;
    drawAttendance(c);
    if (state.detail === c) renderDetail();
  }
  /* Ein anderer Zeitraum der Klassenleitung wird über seine Fehlzeiten
     ersetzt, ohne die Abwesenheit dieser Stunde anzufassen. */
  const fremd = !!(item && ablManage && !item.current && (item.ids || []).length);
  try {
    await cbPost(c, {
      action:'attendance', student_id:String(rec.id), state: spaet ? 'late' : 'absent',
      note:(d.text || '').trim(), arrival: spaet ? d.toTime : '', reason:d.reason || '',
      from_date:d.fromDate, from_time:d.fromTime, to_date:d.toDate, to_time:d.toTime,
      notified: d.notified ? '1' : '0',
      replace: fremd ? item.ids.join(',') : '',
      scope: fremd ? 'other' : ''
    });
    return true;
  } catch (e){ toast('Nicht gespeichert: ' + e.message); return false; }
}

/* Der Haken oben rechts: sichern und schließen. */
async function absSave(){
  const c = absCard, rec = absRec, d = absDraft, item = absItem;
  if (!c || !rec) return;
  absCollect();
  closeAbsence();
  buzz();
  if (await absSchreiben(c, rec, d, item)){
    attRefresh(c);
    if (ablRec === rec) ablLaden();
  }
}

/* Löschen: die Abwesenheit fällt weg. */
async function absDelete(){
  const c = absCard, rec = absRec, item = absItem;
  if (!c || !rec || !item) return;
  if (!confirm('Abwesenheit löschen?')) return;
  const hier = !!item.current;
  if (hier){
    rec.state = 'present';
    rec.arrival = ''; rec.gone = ''; rec.reason = ''; rec.note = '';
    rec.from_date = rec.from_time = rec.to_date = rec.to_time = '';
    rec.notified = false;
  }
  closeAbsence();
  if (hier){ drawAttendance(c); if (state.detail === c) renderDetail(); }
  try {
    await cbPost(c, { action:'absenceDelete', student_id:String(rec.id),
                      ids:(item.ids || []).join(','), current: hier ? '1' : '0' });
    attRefresh(c);
    if (ablRec === rec) ablLaden();
  } catch (e){ toast('Nicht gelöscht: ' + e.message); }
}

$('#dabsClose').addEventListener('click', closeAbsence);
$('#dabsSave').addEventListener('click', absSave);
$('#dabsDelete').addEventListener('click', absDelete);
document.addEventListener('click', e => {
  const pick = $('#dabsPick');
  if (!pick || pick.hidden) return;
  if (!e.target.closest('#dabsPick') && !e.target.closest('#absReason')) pick.hidden = true;
});

function closeClassbook(){
  const idx = document.getElementById('attIndex'); if (idx) idx.remove();
  $('#detailSub').classList.remove('dsub--raum');
  closeAbsence(); closeAbsList(); closeForm();
  $('#detailSub').hidden = true;
  cbCurrent = null;
  if (state.detail) renderDetail();
}

$('#dsubBack').addEventListener('click', closeClassbook);
$('#dsubAddTop').addEventListener('click', () => {
  if (cbCurrent === 'homework' || cbCurrent === 'classbook') openItemSheet(cbCurrent);
});

function closeClassbook(){
  const idx = document.getElementById('attIndex'); if (idx) idx.remove();
  $('#detailSub').classList.remove('dsub--raum');
  closeAbsence(); closeAbsList(); closeForm();
  $('#detailSub').hidden = true;
  cbCurrent = null;
  if (state.detail) renderDetail();
}

$('#dsubBack').addEventListener('click', closeClassbook);
$('#dsubAddTop').addEventListener('click', () => {
  if (cbCurrent === 'homework' || cbCurrent === 'classbook') openItemSheet(cbCurrent);
});

$('#dsubSave').addEventListener('click', async () => {
  /* In der Schülerliste bestätigt der Haken die Kontrolle — der Vermerk
     landet in der Datenbank, damit er nach dem Neuladen stehen bleibt. */
  if (cbCurrent === 'students'){
    const c = state.detail; if (!c) return;
    await alleAnwesend(c);
    return;
  }
  const el = $('#dsubText');
  if (el && state.detail && cbCurrent) await cbSaveText(state.detail, cbCurrent, el.value.trim());
  closeClassbook();
});

/* ---------------------------------------------------------------------
   Block auf eine einzelne Stunde umstellen
   ---------------------------------------------------------------------
   Eine Doppelstunde ist im Klassenbuch zwei Stunden. Über die Zeitangabe
   oben lässt sich wählen, worauf sich die Eintragungen beziehen: auf den
   ganzen Block oder auf eine bestimmte Stunde. Der Server bekommt dann
   deren Stundennummer und findet dieselbe Zeile wie der Desktop.
   --------------------------------------------------------------------- */
/* Eintragungen gehören immer zu genau einer Stunde — auch bei einem Block.
   Deshalb arbeitet das Detailfenster grundsätzlich auf einer Einzelstunde;
   voreingestellt ist die erste des Blocks. */
/* Voreingestellt gilt eine Eintragung für den ganzen Block — so wie am
   Desktop, der sie in alle Stunden des Blocks schreibt. Wer will, stellt
   über die Zeitangabe oben auf eine einzelne Stunde um. */
function detailTeilCard(ganz, lesson){
  if (lesson === null) return ganz;
  if ((ganz.span || 1) < 2) return ganz;
  /* Die Kennung des Blocks passt nur zur ersten Stunde. Für jede andere
     sucht der Server die Stunde selbst — genau so wie timetable.php. */
  const eigen = lesson === ganz.lesson ? ganz.entryId : null;
  return Object.assign({}, ganz, { lesson, span:1, entryId:eigen, _teil:lesson });
}

/* Einzelstundenauswahl über das Auswahlmenü im Titel (Datum und Uhrzeit,
   mittig mit Pfeil): nur bei einem Block mit Klassenbuch, nicht für
   Schüler*innen. Der frühere Segmentschalter darunter entfällt. */
function detailTeilbar(ganz){
  return !!ganz && (ganz.span || 1) > 1 && !!ganz.unterricht && !state.isStudent;
}

function detailTeilOeffnen(){
  const ganz = state.detailFull;
  if (!detailTeilbar(ganz)) return;

  const pop = $('#detailPart');
  const zeit = n => {
    const t = state.times[n] || {};
    return `${(t.start || '').replace(/^0/, '')} – ${t.end || ''}`;
  };
  /* Kein Teil gewählt = ganzer Block. */
  const jetzt = state.detail._teil ? state.detail.lesson : null;
  const bis = (state.times[Math.min(ganz.lesson + ganz.span - 1, 12)] || {}).end || '';

  const eigen = new Set(((cbTeil && cbTeil.parts) || []).filter(t => t.own).map(t => t.lesson));
  let html = `<button type="button" role="option" data-l="" class="${jetzt === null ? 'is-on' : ''}" aria-selected="${jetzt === null}">
                <b>Ganzer Block</b>
                <span>${esc((zeit(ganz.lesson).split('–')[0] || '').trim())} – ${esc(bis)}</span>
              </button>`;
  for (let i = 0; i < ganz.span; i++){
    const n = ganz.lesson + i;
    html += `<button type="button" role="option" data-l="${n}" class="${jetzt === n ? 'is-on' : ''}" aria-selected="${jetzt === n}">
               <b>${n}. Stunde${eigen.has(n) ? '<i class="dteil__punkt" aria-label="eigene Einträge"></i>' : ''}</b><span>${esc(zeit(n))}</span>
             </button>`;
  }
  pop.innerHTML = html;
  pop.hidden = false;

  $$('#detailPart button').forEach(b => b.addEventListener('click', () => {
    pop.hidden = true;
    detailTeilWaehlen(b.dataset.l === '' ? null : +b.dataset.l);
  }));
}

/* Auf den ganzen Block (null) oder eine Einzelstunde umstellen. */
function detailTeilWaehlen(v){
  const ganz = state.detailFull;
  if (!detailTeilbar(ganz)) return;
  const jetztTeil = state.detail._teil || null;
  if (v === jetztTeil) return;
  const neu = detailTeilCard(ganz, v);
  state.detail = neu;
  cbCache = {};
  cbTeil = null;
  attReset(neu, true);
  $('#detailSub').hidden = true;
  closeAbsence(); closeForm();
  cbCurrent = null;
  renderDetail();
  cbLoadAll(neu).then(() => { if (state.detail === neu) renderDetail(); });
}

/* Früher stand unter dem Kopf ein Segmentschalter „Ganzer Block“ / „n. Stunde“.
   Die Auswahl steckt jetzt im Auswahlmenü des Titels (detailTeilOeffnen);
   die Leiste bleibt leer und verborgen. */
function renderDetailTeil(){
  const box = $('#detailTeil');
  if (!box) return;
  box.hidden = true;
  box.innerHTML = '';
}
$('#detailTeil').addEventListener('click', e => {
  const b = e.target.closest('button[data-l]');
  if (!b) return;
  detailTeilWaehlen(b.dataset.l === '' ? null : +b.dataset.l);
});

$('#detailWhen').addEventListener('click', e => {
  e.stopPropagation();
  const pop = $('#detailPart');
  if (!pop.hidden){ pop.hidden = true; return; }
  detailTeilOeffnen();
});
document.addEventListener('click', e => {
  const pop = $('#detailPart');
  if (pop && !pop.hidden && !e.target.closest('#detailPart') && !e.target.closest('#detailWhen'))
    pop.hidden = true;
});

/* Umschalten zwischen gleichzeitigen Stunden. Die Karten liegen an der
   Leiste selbst, damit hier nichts erneut gesucht werden muss. */
$('#detailTabs').addEventListener('click', e => {
  const t = e.target.closest('.dtabs__t');
  if (!t) return;
  const liste = $('#detailTabs')._cards || [];
  const ziel = liste[+t.dataset.tab];
  if (!ziel || ziel === state.detail) return;
  state.detail = ziel;
  state.detailFull = ziel;      /* neue Stunde, neuer ganzer Block */
  renderDetail();
});

$('#detailClose').addEventListener('click', () => {
  closeAbsence(); closeForm();
  $('#detailPart').hidden = true;
  $('#detailSub').hidden = true;
  SH.detail.close();
  /* Wurde etwas eingetragen, ändert das den Plan: ein Block kann sich
     teilen, eine Kachel einen Punkt bekommen. Das soll man sofort sehen
     und nicht erst nach dem nächsten Laden. */
  planNachfuehren();
});

/* Die Woche still nachladen und neu zeichnen. */
function planNachfuehren(){
  state.around = {};
  refresh();
}
/* Über die drei Punkte in die Pläne der beteiligten Klasse, Lehrkraft
   oder des Raums wechseln. */
function findEntity(type, label){
  if (!label) return null;
  const want = String(label).trim().toUpperCase();
  if (!want) return null;
  return state.entities.find(e => e.type === type && e.label.toUpperCase() === want) || null;
}

function renderDetailMenu(){
  const c = state.detail;
  const pop = $('#detailMenuPop');
  if (!c){ pop.hidden = true; return; }

  /* Klasse: Gruppe vor der Tafel. Lehrkraft: Person mit Zeigestock an der
     Tafel. Raum: Tür mit Klinke. */
  /* Die Symbole der Vorbild-Sammlung. Sie sind Füllflächen und brauchen
     deshalb `uic` statt `ico` — dort steht Kontur an, Füllung aus. */
  const ICO = {
    class:   'ic-shared-classentity',
    teacher: 'ic-shared-teacherentity',
    room:    'ic-shared-roomentity'
  };
  const LBL = { class:'Klasse', teacher:'Lehrkraft', room:'Raum' };

  /* Sowohl die aktuelle als auch die abwesende (ursprüngliche) Lehrkraft,
     Raum bzw. Klasse anbieten — man kann beider Plan öffnen. */
  const wanted = [
    ['class',   c.class],   ['class',   c.classOrig],
    ['teacher', c.teacher], ['teacher', c.teacherOrig],
    ['room',    c.room],    ['room',    c.roomOrig]
  ];

  /* Bei einer Kopplung gehören auch die Mitkoppelnden dazu — im Feld
     `teacher` steht nur die eigene Lehrkraft. */
  (koppTokens(c) || []).forEach(t => {
    wanted.push(['teacher', t.text]);
    if (t.for) wanted.push(['teacher', t.for]);
  });

  /* Dasselbe für gekoppelte Klassen. Im Feld `class` stehen sie zwar
     durch Komma getrennt und würden beim Auftrennen mitkommen — aber
     nur, solange dort wirklich alle stehen. Aus der Liste kommen sie
     verlässlich, auch die abwesende. */
  (Array.isArray(c.classList) ? c.classList : []).forEach(t => {
    if (t && t.name) wanted.push(['class', t.name]);
  });

  const items = [];
  wanted.forEach(([type, label]) => {
    /* Kopplungen wie "PK/WW" auftrennen */
    String(label || '').split(/[\/,;]+/).map(x => x.trim()).filter(Boolean).forEach(one => {
      const hit = findEntity(type, one);
      if (hit && !items.some(i => i.e === hit)) items.push({ e:hit, type, label:one });
    });
  });

  pop.innerHTML = items.length
    ? `<div class="dmenu__head">Im Stundenplan anzeigen</div>`
      + items.map((i, n) => `<button data-n="${n}">
         <svg class="uic ico"><use href="#${ICO[i.type] || 'ic-shared-classentity'}"></use></svg>
         <span>${esc(i.label)}</span>
       </button>`).join('')
    : '<div class="dmenu__empty">Keine weiteren Pläne zu dieser Stunde.</div>';

  $$('#detailMenuPop button').forEach(b => b.addEventListener('click', () => {
    const pick = items[+b.dataset.n];
    pop.hidden = true;
    /* In die Woche der geöffneten Stunde springen */
    state.weekStart = iso(mondayOf(parseISO(c.date)));
    SH.detail.close();
    planWechseln(pick.e);
  }));
}

$('#detailMenu').addEventListener('click', e => {
  e.stopPropagation();
  const pop = $('#detailMenuPop');
  if (pop.hidden){ renderDetailMenu(); pop.hidden = false; }
  else pop.hidden = true;
});
document.addEventListener('click', e => {
  const pop = $('#detailMenuPop');
  if (!pop || pop.hidden) return;
  if (!e.target.closest('#detailMenuPop') && !e.target.closest('#detailMenu')) pop.hidden = true;
});

/* Beim Wischen nie versehentlich eine Stunde öffnen: Hat sich der Finger
   mehr als 8 px bewegt, rollt das Raster gerade oder ist das Rollen erst
   einen Augenblick her, wird der folgende Tipp auf eine Kachel verworfen. */
const rasterGeste = { x:0, y:0, bewegt:false, rollEnde:0, beruehrt:-1e9 };
(() => {
  const area = $('#gridScroll');
  if (!area) return;
  area.addEventListener('touchstart', e => {
    const t = e.touches[0]; if (!t) return;
    rasterGeste.x = t.clientX; rasterGeste.y = t.clientY;
    rasterGeste.bewegt = e.touches.length > 1;
    rasterGeste.beruehrt = performance.now();
  }, { passive:true });
  area.addEventListener('touchend', () => { rasterGeste.beruehrt = performance.now(); }, { passive:true });
  area.addEventListener('touchmove', e => {
    const t = e.touches[0]; if (!t || rasterGeste.bewegt) return;
    if (Math.abs(t.clientX - rasterGeste.x) > 8 || Math.abs(t.clientY - rasterGeste.y) > 8) rasterGeste.bewegt = true;
  }, { passive:true });
  /* Nur Rollen durch den Finger (auch der Nachlauf danach) zählt — nicht
     das Setzen der Rollposition durch die App selbst. */
  area.addEventListener('scroll', () => {
    const t = performance.now();
    if (t - rasterGeste.beruehrt < 2500) rasterGeste.rollEnde = t;
  }, { passive:true });
  /* Mausbedienung: Ziehen mit gedrückter Taste zählt ebenso. */
  area.addEventListener('pointerdown', e => {
    if (e.pointerType === 'touch') return;
    rasterGeste.x = e.clientX; rasterGeste.y = e.clientY; rasterGeste.bewegt = false;
  }, { passive:true });
  area.addEventListener('pointermove', e => {
    if (e.pointerType === 'touch' || !e.buttons || rasterGeste.bewegt) return;
    if (Math.abs(e.clientX - rasterGeste.x) > 8 || Math.abs(e.clientY - rasterGeste.y) > 8) rasterGeste.bewegt = true;
  }, { passive:true });
})();
const rasterTippGueltig = () => !rasterGeste.bewegt
  && performance.now() - rasterGeste.rollEnde > 180
  && !document.querySelector('#colStrip.is-dragging');

document.addEventListener('click', e => {
  if (e.target.closest('#gridPane') && !rasterTippGueltig()){ e.stopPropagation(); return; }
  const more = e.target.closest('#gridPane .parcount');
  if (more){ setView('day', more.dataset.more); return; }

  const card = e.target.closest('#gridPane .card');
  if (card) openDetail(card.dataset.date, +card.dataset.lesson, +(card.dataset.idx || 0));
  const absBand = e.target.closest('#gridPane .absband');
  if (absBand){
    const b = (state.week.bands || []).find(x => x.date === absBand.dataset.band);
    if (b) toast(b.info || 'Abwesenheit');
  }
});

/* =====================================================================
   Einstellungen
   ===================================================================== */
function bindOpt(sel, key, after){
  const el = $(sel);
  el.checked = state.opts[key];
  el.addEventListener('change', () => {
    state.opts[key] = el.checked;
    store.set(key, el.checked);
    after && after();
  });
}
bindOpt('#optCardTimes', 'cardTimes', applyCardTimes);
bindOpt('#optLongNames', 'longNames', () => state.detail && renderDetail());
bindOpt('#optShowCancelled', 'showCancelled', renderGrid);
function applyCardTimes(){ document.body.classList.toggle('show-card-times', state.opts.cardTimes); }

/* Wer ausdrücklich auffrischt, will neue Daten sehen — der Speicher wird
   dafür geleert. */
$('#btnRefresh').addEventListener('click', () => {
  wochenVergessen(); refresh(); toast('Aktualisiert'); syncLabel();
});
function syncLabel(){
  const t = store.get('lastSync', null);
  $('#lastSync').textContent = state.source === 'db' || state.source === 'scrape'
    ? (t ? new Date(t).toLocaleTimeString('de-DE', { hour:'2-digit', minute:'2-digit' }) : 'noch nicht geladen')
    : 'Demodaten';
}

/* =====================================================================
   Anmeldung
   ===================================================================== */
const LOGIN_URL = new URL('api/login', location.href).toString();

function showLogin(msg){
  $('#login').hidden = false;
  $('#app').style.visibility = 'hidden';
  if (msg) loginError(msg);
  setTimeout(() => $('#loginUser').focus(), 120);
}
function hideLogin(){
  $('#login').hidden = true;
  $('#app').style.visibility = '';
}
function loginError(msg){
  const el = $('#loginError');
  el.textContent = msg || '';
  el.hidden = !msg;
}

async function doLogin(){
  const user = $('#loginUser').value.trim();
  const pass = $('#loginPass').value;
  if (!user || !pass){ loginError('Bitte Benutzername und Passwort eingeben.'); return; }

  const btn = $('#loginSubmit');
  btn.disabled = true; btn.textContent = 'Anmelden …';
  loginError('');
  try {
    const body = new URLSearchParams({ action:'login', username:user, password:pass });
    const res = await fetch(LOGIN_URL, {
      method:'POST', credentials:'same-origin',
      headers:{ 'Content-Type':'application/x-www-form-urlencoded' },
      body
    });
    const data = await res.json().catch(() => ({}));
    if (res.ok && data.ok){
      /* Neu laden: die Seite bringt die Woche dann gleich als Livedaten mit.
         Kam ein weiteres Profil hinzu, gehören die gemerkten Wochen dem
         bisherigen Konto. */
      if ($('#login').classList.contains('is-add'))
        try { const th = localStorage.getItem('tt.theme'); localStorage.clear(); if (th) localStorage.setItem('tt.theme', th); } catch (e2){}
      location.reload();
      return;
    }
    if (res.status === 429)
      loginError(`Zu viele Versuche. Bitte ${data.retryIn || 60} Sekunden warten.`);
    else if (res.status === 401 && data.error && data.error !== 'not_authenticated')
      loginError(data.error);
    else if (res.status === 401)
      loginError('Benutzername oder Passwort stimmt nicht.');
    else
      loginError((data && data.error) ? data.error : 'Anmeldung nicht möglich. Bitte später erneut versuchen.');
  } catch (e){
    loginError('Keine Verbindung zum Server.');
  }
  btn.disabled = false; btn.textContent = 'Anmelden';
}

$('#loginSubmit').addEventListener('click', doLogin);
['#loginUser', '#loginPass'].forEach(sel =>
  $(sel).addEventListener('keydown', e => { if (e.key === 'Enter') doLogin(); }));

$('#btnLogout').addEventListener('click', async () => {
  /* Benachrichtigungen dieses Profils enden mit der Abmeldung. */
  try { await pushAus(true); } catch (e){}
  let r = null;
  try {
    r = await fetch(LOGIN_URL, {
      method:'POST', credentials:'same-origin',
      headers:{ 'Content-Type':'application/x-www-form-urlencoded' },
      body: new URLSearchParams({ action:'logout' })
    }).then(x => x.json());
  } catch (e){}
  try { const th = localStorage.getItem('tt.theme'); localStorage.clear(); if (th) localStorage.setItem('tt.theme', th); } catch (e){}
  /* nach dem Abmelden nicht sofort wieder automatisch über UntPlanWeb
     anmelden — außer ein weiteres Profil des Geräts hat übernommen */
  if (!(r && r.switched)) try { sessionStorage.setItem('sso:versucht', '1'); } catch (e){}
  location.reload();
});

/* =====================================================================
   Tastatur: Sheets verkürzen, damit Treffer nicht darunter verschwinden
   ===================================================================== */
(() => {
  const vv = window.visualViewport;
  if (!vv) return;
  /* Nach dem Schließen der Tastatur darf nichts hängen bleiben: --kb und
     --dkb gehen auf null (auch wenn das Detail inzwischen zu ist), und das
     Fenster wird wieder an seinen Platz gezogen, falls iOS es beim Fokus
     hochgeschoben hatte. Kleine Ausschläge (Adressleiste, Rundung) zählen
     nicht als Tastatur. Geschrieben wird nur bei einer echten Änderung,
     gebündelt in einem Bildschirmtakt. */
  const root = document.documentElement;
  let lastKb = -1, lastDkb = -1, frame = 0;
  const eingabeAktiv = () => {
    const a = document.activeElement;
    return !!a && (a.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(a.tagName));
  };
  const apply = () => {
    frame = 0;
    let kb = Math.max(0, window.innerHeight - vv.height - vv.offsetTop);
    if (kb < 60 || !eingabeAktiv()) kb = 0;
    const blatt = document.getElementById('sheetDetail');
    let dkb = 0;
    if (kb > 0 && blatt && !blatt.hidden){
      /* Stundendetail: Das Blatt behält seine Größe und seinen Platz — die
         Tastatur legt sich darüber. Wie weit sie in das Blatt hineinragt,
         steht in --dkb; um so viel bekommt nur der Inhalt unten Luft und
         rollt innen, statt dass das Blatt springt oder sich staucht. */
      const r = blatt.getBoundingClientRect();
      dkb = Math.max(0, r.bottom - (vv.offsetTop + vv.height));
    }
    kb = Math.round(kb); dkb = Math.round(dkb);
    if (kb !== lastKb){ root.style.setProperty('--kb', kb + 'px'); lastKb = kb; }
    if (dkb !== lastDkb && blatt){ blatt.style.setProperty('--dkb', dkb + 'px'); lastDkb = dkb; }
    /* Tastatur weg: hochgeschobenes Fenster zurückholen. */
    if (kb === 0 && (window.scrollY || vv.offsetTop || (document.scrollingElement || {}).scrollTop))
      pinViewport();
  };
  const plan = () => { if (!frame) frame = requestAnimationFrame(apply); };
  /* iOS meldet das Ende der Tastatur-Animation nicht zuverlässig — darum
     mehrfach nachsehen, bis sie wirklich fort ist. */
  const nachlauf = () => { plan(); [80, 260, 520].forEach(t => setTimeout(plan, t)); };
  document.addEventListener('focusin', nachlauf, { passive:true });
  document.addEventListener('focusout', nachlauf, { passive:true });
  vv.addEventListener('resize', plan, { passive:true });
  vv.addEventListener('scroll', plan, { passive:true });
  window.addEventListener('orientationchange', nachlauf, { passive:true });
  apply();
})();

/* Beim Tippen den ersten Treffer in Sicht holen */
$('#pickerSearch').addEventListener('focus', () => {
  setTimeout(() => $('#pickerList').scrollTop = 0, 250);
});

/* =====================================================================
   Toast
   ===================================================================== */
let toastTimer;
function toast(msg){
  if (toastQuiet) return;
  if (!msg) return;
  const el = $('#toast');
  el.textContent = msg; el.hidden = false;
  requestAnimationFrame(() => el.classList.add('is-on'));
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => {
    el.classList.remove('is-on');
    setTimeout(() => { el.hidden = true; }, 220);
  }, 2400);
}

/* =====================================================================
   PWA
   ===================================================================== */
let deferredPrompt = null;
window.addEventListener('beforeinstallprompt', e => {
  e.preventDefault(); deferredPrompt = e; $('#btnInstall').hidden = false;
});
$('#btnInstall').addEventListener('click', async () => {
  if (!deferredPrompt) return;
  deferredPrompt.prompt();
  await deferredPrompt.userChoice;
  deferredPrompt = null;
  $('#btnInstall').hidden = true;
});
/* Die Fassung setzt der Server ein — derselbe Prüfwert wie im Cachenamen von sw.js. */
const BUILD = window.TT_FASSUNG || 'entwicklung';

if ('serviceWorker' in navigator){
  window.addEventListener('load', async () => {
    try {
      const reg = await navigator.serviceWorker.register('sw.js');
      reg.update();

      /* Steht eine neue Fassung bereit, wird sie sofort übernommen und die
         Seite einmal neu geladen. Ohne das liefert der alte Cache weiter
         die alten Dateien aus. */
      let reloading = false;
      navigator.serviceWorker.addEventListener('controllerchange', () => {
        if (reloading) return;
        reloading = true;
        location.reload();
      });
      reg.addEventListener('updatefound', () => {
        const sw = reg.installing;
        if (!sw) return;
        sw.addEventListener('statechange', () => {
          if (sw.state === 'installed' && navigator.serviceWorker.controller)
            sw.postMessage('skip-waiting');
        });
      });
    } catch (e){}
  });
}

/* Jetzt-Linie nachführen */
/* Im nächsten Bildschirmtakt zeichnen und mehrere Anstöße bündeln. Nur
   wenn die App sichtbar ist — im Hintergrund gibt es nichts zu sehen. */
let rasterTakt = 0;
function rasterBald(){
  if (rasterTakt || document.hidden) return;
  rasterTakt = requestAnimationFrame(() => {
    rasterTakt = 0;
    if (state.screen === 'plan' && state.week) renderGrid();
  });
}
setInterval(rasterBald, 60000);

/* Bei Rückkehr in die App auf die aktuelle Woche prüfen */
document.addEventListener('visibilitychange', () => {
  if (!document.hidden && state.week) rasterBald();
});

/* =====================================================================
   Start
   ===================================================================== */
(async function init(){
  if (window.TT_BOOT && window.TT_BOOT.needLogin){
    /* Einmalanmeldung: einmal je Sitzung automatisch über UntPlanWeb anmelden – wer dort angemeldet ist,
       kommt ohne Passwort direkt in den Plan. Schlägt das fehl, bleibt die normale Anmeldung. */
    const fehler = new URLSearchParams(location.search).get('sso') === 'fehler';
    let versucht = false;
    try { versucht = sessionStorage.getItem('sso:versucht') === '1'; sessionStorage.setItem('sso:versucht', '1'); } catch (e) { versucht = true; }
    if (!fehler && !versucht){ location.replace('sso/start'); return; }
    if (fehler) history.replaceState(null, '', location.pathname);
    showLogin(fehler ? 'Die Anmeldung über UntPlanWeb hat nicht geklappt. Bitte erneut versuchen oder mit Passwort anmelden.' : '');
    return;
  }
  try { sessionStorage.removeItem('sso:versucht'); } catch (e) { /* privat */ }
  applyCardTimes();
  syncLabel();
  const mineLbl = $('#pickerMineLabel');
  if (mineLbl && state.entities[0]) mineLbl.textContent = state.entities[0].label || 'Mein Stundenplan';
  /* Auf der Profilseite steht der Mensch, nicht der Plan — „Mein
     Stundenplan" gehört über den Plan, nicht hierher. */
  /* Fällt der volle Name aus, tritt das Kürzel an seine Stelle — leer
     bleibt die Zeile nie. */
  /* Fällt der volle Name aus, greifen der Reihe nach: eigener Eintrag,
     erster Eintrag der Auswahl, zuletzt das Kürzel. Leer bleibt es nie. */
  const eigen = state.entities.find(e => e.type === 'mine') || state.entities[0] || {};
  const name = state.ownName || (state.own && state.own.info) || eigen.info
             || state.ownCode || eigen.code || eigen.label || '';
  const kurz = state.ownCode || eigen.code || '';
  /* Wie im Vorbild: groß das Kürzel, in der Kontokarte die Anmeldung mit
     der Rolle als Plakette. Ohne Kürzel (Administration) steht der Name
     oben, ohne Anmeldenamen der Name in der Karte. */
  const login = (state.user && state.user.login) || '';
  $('#profileName').textContent = login || name || kurz || 'Profil';
  const full = $('#profileFullName');
  if (full) full.textContent = kurz || name || 'Profil';
  const btnKonto = $('#btnSwitchAccount');
  if (btnKonto && name) btnKonto.title = name;
  /* Ob Schüler*in oder Lehrkraft, sagt die Art des eigenen Plans — ein
     Klassenplan gehört einer Schülerin, ein Lehrerplan einer Lehrkraft. */
  /* Die Art des eigenen Plans entscheidet: Klassenplan → Schüler*in,
     Lehrerplan → Lehrkraft. Das steht vom Laden an fest. */
  const art = state.ownKind || (state.own && state.own.kind) || eigen.kind || '';
  const istSchueler = art ? art === 'class' : !!state.isStudent;
  $('#profileRole').textContent = (state.user && state.user.role)
    || (istSchueler ? 'Schüler*in' : 'Lehrkraft');
  $('#profileAvatar').textContent = (login || name || kurz || '?').trim().charAt(0).toUpperCase();
  renderMessages();

  /* Ohne Bootdaten (z. B. beim reinen Vorschaubetrieb) den Endpunkt fragen. */
  if (!BOOT && state.source !== 'demo-forced' && !TT_API.isLive()){
    try {
      const m = await TT_DB.meta();
      state.source = 'db';
      if (m.lessonTimes && Object.keys(m.lessonTimes).length) state.times = m.lessonTimes;
      if (m.own && m.own.kind) state.kind = m.own.kind;

      const own = { type:'mine', id:'me', label:'Mein Stundenplan',
                    code:m.own?.label || '', info:m.own?.info || '', kind:m.own?.kind || '' };
      state.isStudent = !!m.isStudent || (m.own?.kind === 'class');
      state.ownName = m.own?.info || '';
      state.ownCode = m.own?.label || '';
      state.ownKind = m.own?.kind || '';
      state.user = m.user || state.user || null;
      state.entities = [own, ...(m.entities || [])];

      const saved = store.get('entity', null);
      state.entity = state.entities.find(e => saved && e.type === saved.type && String(e.id) === String(saved.id)) || own;

      $('#profileName').textContent = own.label;
      $('#profileRole').textContent = own.info || '';
      $('#profileAvatar').textContent = (own.label || '?').slice(0, 2).toUpperCase();
    } catch (e){
      state.source = 'demo';
    }
  }

  if (TT_API.isLive()){
    state.source = 'scrape';
    try { state.entities = await TT_API.fetchEntities(); } catch {}
  }

  await refresh();

  syncLabel();
  const stampEl = $('#buildStamp');
  if (stampEl) stampEl.textContent = BUILD;

  const view = qs.get('view');
  if (view === 'msg') showScreen('msg');
  else if (view === 'today') showScreen('start');

  scrollToNow();
  planwachtStart();
})();

/* =====================================================================
   Benachrichtigungen bei Änderungen im eigenen Plan
   ---------------------------------------------------------------------
   Für alle mit eigenem Plan (Lehrkräfte, Klassen). Wo der Browser Web-Push
   kann (Chrome, Edge, Firefox, Safari; am iPhone/iPad die App vom
   Homescreen ab iOS 16.4), abonniert die App beim Server: UntPlanMobile
   prüft jede Minute den Planstand von UntPlanWeb und schickt sofort nach
   einem Import eine Nachricht, wenn sich im eigenen Plan etwas ändert.
   Ohne Push merkt sich die App den Stand der laufenden und der nächsten
   Woche und vergleicht ihn beim Öffnen, beim Zurückkehren, alle 15
   Minuten und — wo möglich — im Hintergrund (periodic background sync).
   ===================================================================== */
function planwachtMoeglich(){
  /* Wer einen eigenen Plan hat (Lehrkraft oder Klasse), kann sich über
     Änderungen darin benachrichtigen lassen. */
  const eigen = (BOOT && BOOT.own && BOOT.own.id) || state.ownCode;
  return state.source === 'db' && !!eigen && !!window.PlanWacht;
}
function planwachtUnterstuetzt(){
  return 'Notification' in window && 'serviceWorker' in navigator && 'caches' in window;
}
/* Echtes Web-Push: der Server meldet sich sofort nach einem Import. */
function pushUnterstuetzt(){
  return 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
}
function planwachtKonto(){ return state.ownCode || (state.user && state.user.login) || ''; }
const planwachtAn = () => !!store.get('notify', false)
  && planwachtUnterstuetzt() && Notification.permission === 'granted';

const PUSH_URL = new URL('api/push', location.href).href;
async function pushApi(action, felder){
  const r = await fetch(PUSH_URL + (felder ? '' : '?action=' + action), felder ? {
    method:'POST', credentials:'same-origin', cache:'no-store',
    headers:{ 'Content-Type':'application/x-www-form-urlencoded' },
    body:new URLSearchParams(Object.assign({ action }, felder))
  } : { credentials:'same-origin', cache:'no-store' });
  const d = await r.json().catch(() => null);
  if (!r.ok || !d || d.ok === false) throw new Error((d && d.error) || ('HTTP ' + r.status));
  return d;
}
function b64urlBytes(s){
  const b = atob(String(s).replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(s.length / 4) * 4, '='));
  return Uint8Array.from(b, c => c.charCodeAt(0));
}
/* Registrierung des Service Workers – `ready` wartet ohne ihn ewig. */
async function swBereit(){
  const reg = await navigator.serviceWorker.getRegistration();
  if (reg && reg.active) return reg;
  return await Promise.race([navigator.serviceWorker.ready, new Promise(r => setTimeout(() => r(null), 6000))]);
}
/* Abo des Geräts anlegen (bzw. das vorhandene nehmen) und beim Server hinterlegen. */
async function pushAbonnieren(key){
  const reg = await swBereit();
  if (!reg || !reg.pushManager) throw new Error('Service Worker nicht bereit');
  let sub = await reg.pushManager.getSubscription();
  if (sub && sub.options && sub.options.applicationServerKey){
    /* Anderer Serverschlüssel (z. B. neu eingerichtet): neu abonnieren. */
    const alt = new Uint8Array(sub.options.applicationServerKey), neu = b64urlBytes(key);
    if (alt.length !== neu.length || alt.some((x, i) => x !== neu[i])){ await sub.unsubscribe(); sub = null; }
  }
  if (!sub) sub = await reg.pushManager.subscribe({ userVisibleOnly:true, applicationServerKey: b64urlBytes(key) });
  await pushApi('subscribe', { subscription: JSON.stringify(sub.toJSON ? sub.toJSON() : sub) });
  return sub;
}
/* Für das laufende Profil ausschalten; bekommt kein Profil des Geräts
   mehr Nachrichten, wird das Abo des Browsers gekündigt. */
async function pushAus(still){
  if (!pushUnterstuetzt() || state.source !== 'db') return;
  let andere = false;
  try { andere = !!(await pushApi('off', {})).others; } catch (e){ if (!still) throw e; }
  if (andere) return;
  try {
    const reg = await navigator.serviceWorker.getRegistration();
    const sub = reg && reg.pushManager ? await reg.pushManager.getSubscription() : null;
    if (sub){
      const endpoint = sub.endpoint;
      await sub.unsubscribe();
      await pushApi('unsubscribe', { endpoint }).catch(() => null);
    }
  } catch (e){ /* nichts weiter */ }
}

async function planwachtHintergrund(an){
  try {
    /* getRegistration statt ready: ready wartet ohne Service Worker ewig. */
    const reg = await navigator.serviceWorker.getRegistration();
    if (!reg || !reg.periodicSync) return false;
    if (!an){ await reg.periodicSync.unregister('planwacht'); return false; }
    const p = navigator.permissions
      ? await navigator.permissions.query({ name:'periodic-background-sync' }).catch(() => null) : null;
    if (p && p.state === 'denied') return false;
    await reg.periodicSync.register('planwacht', { minInterval: 60 * 60 * 1000 });
    return true;
  } catch (e){ return false; }
}

function planwachtHinweis(text){
  const h = $('#notifyHint'); if (h) h.textContent = text;
}

let planwachtZuletzt = 0;
let pushAktiv = false;
function planwachtPruefen(){
  /* Mit Web-Push meldet sich der Server; die Prüfung in der App entfällt. */
  if (pushAktiv || !planwachtMoeglich() || !planwachtAn()) return;
  if (Date.now() - planwachtZuletzt < 60 * 1000) return;   /* nicht doppelt kurz hintereinander */
  planwachtZuletzt = Date.now();
  window.PlanWacht.pruefen().catch(() => null);
}

function planwachtStart(){
  const row = $('#rowNotify'), el = $('#optNotify');
  if (!row || !el) return;
  row.hidden = !planwachtMoeglich();
  if (row.hidden){
    /* Kein eigener Plan (mehr): eine früher eingeschaltete Wache ruht. */
    if (window.PlanWacht && state.source === 'db' && store.get('notify', false)){
      store.set('notify', false);
      window.PlanWacht.einstellen(false, planwachtKonto()).catch(() => null);
      if ('serviceWorker' in navigator) planwachtHintergrund(false);
    }
    return;
  }

  const ios = /iPhone|iPad|iPod/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
  if (!planwachtUnterstuetzt()){
    el.checked = false; el.disabled = true;
    /* iPhone und iPad: Push gibt es nur in der App vom Homescreen (ab iOS 16.4). */
    planwachtHinweis(ios ? 'Nur in der App vom Homescreen (ab iOS 16.4)' : 'Von diesem Browser nicht unterstützt');
    return;
  }
  if (Notification.permission === 'denied') planwachtHinweis('Im Browser nicht erlaubt');

  if (pushUnterstuetzt()){
    /* ---- Web-Push ------------------------------------------------------ */
    let key = '';
    el.checked = false;
    pushApi('status').then(async st => {
      key = st.key;
      pushAktiv = !!st.on && Notification.permission === 'granted';
      el.checked = pushAktiv;
      if (pushAktiv){
        planwachtHinweis('Sofort bei Änderungen im eigenen Plan');
        /* Abo fehlt dem Server oder dem Browser (neu installiert)? Nachholen. */
        try {
          const reg = await navigator.serviceWorker.getRegistration();
          const sub = reg && reg.pushManager ? await reg.pushManager.getSubscription() : null;
          if (!sub || !st.subscribed) await pushAbonnieren(key);
        } catch (e){ /* beim nächsten Start erneut */ }
        /* Die lokale Wache wird nicht mehr gebraucht. */
        if (store.get('notify', false)){ store.set('notify', false); window.PlanWacht.einstellen(false, planwachtKonto()).catch(() => null); planwachtHintergrund(false); }
      } else if (st.on){
        planwachtHinweis('Im Browser nicht erlaubt');
      }
    }).catch(() => { el.disabled = true; planwachtHinweis('Derzeit nicht erreichbar'); });

    el.addEventListener('change', async () => {
      if (el.checked){
        let erlaubnis = Notification.permission;
        if (erlaubnis === 'default'){
          try { erlaubnis = await Notification.requestPermission(); } catch (e){ erlaubnis = 'denied'; }
        }
        if (erlaubnis !== 'granted'){
          el.checked = false;
          planwachtHinweis('Im Browser nicht erlaubt');
          toast('Benachrichtigungen sind für diese Seite nicht erlaubt');
          return;
        }
        try {
          if (!key) key = (await pushApi('status')).key;
          await pushAbonnieren(key);
          pushAktiv = true;
          planwachtHinweis('Sofort bei Änderungen im eigenen Plan');
          toast('Benachrichtigungen eingeschaltet');
        } catch (e){
          el.checked = false;
          planwachtHinweis(ios && !window.navigator.standalone ? 'Nur in der App vom Homescreen (ab iOS 16.4)' : 'Einschalten nicht möglich');
          toast('Benachrichtigungen nicht möglich: ' + e.message);
        }
      } else {
        try { await pushAus(false); pushAktiv = false; planwachtHinweis('Bei Änderungen im eigenen Plan'); }
        catch (e){ el.checked = true; toast('Nicht gespeichert: ' + e.message); }
      }
    });
    return;
  }

  /* ---- Ohne Push: Prüfung in der App (beim Öffnen, im Hintergrund wo möglich) */
  el.checked = planwachtAn();
  el.addEventListener('change', async () => {
    if (el.checked){
      let erlaubnis = Notification.permission;
      if (erlaubnis === 'default'){
        try { erlaubnis = await Notification.requestPermission(); } catch (e){ erlaubnis = 'denied'; }
      }
      if (erlaubnis !== 'granted'){
        el.checked = false;
        store.set('notify', false);
        planwachtHinweis('Im Browser nicht erlaubt');
        toast('Benachrichtigungen sind für diese Seite nicht erlaubt');
        return;
      }
      store.set('notify', true);
      await window.PlanWacht.einstellen(true, planwachtKonto());
      /* Stand merken, ohne zu melden — ab jetzt zählt jede Änderung. */
      await window.PlanWacht.pruefen({ stumm:true }).catch(() => null);
      const hinten = await planwachtHintergrund(true);
      planwachtHinweis(hinten ? 'Bei Änderungen im eigenen Plan, auch im Hintergrund'
                              : 'Bei Änderungen im eigenen Plan (beim Öffnen der App)');
      toast('Benachrichtigungen eingeschaltet');
    } else {
      store.set('notify', false);
      await window.PlanWacht.einstellen(false, planwachtKonto());
      await planwachtHintergrund(false);
      planwachtHinweis('Bei Änderungen im eigenen Plan');
    }
  });

  if (planwachtAn()){
    /* Konto gewechselt? Dann beginnt die Wache mit leerem Stand. */
    window.PlanWacht.einstellen(true, planwachtKonto())
      .then(() => planwachtHintergrund(true))
      .then(() => planwachtPruefen());
  }
  document.addEventListener('visibilitychange', () => { if (!document.hidden) planwachtPruefen(); });
  setInterval(() => { if (!document.hidden) planwachtPruefen(); }, 15 * 60 * 1000);
}
})();

/* Der Reiter „Stundenplan“ trägt wie in der Vorlage die heutige Tageszahl
   im Kalendersymbol — beim Start, beim Zurückkommen zur App und nach
   Mitternacht neu gesetzt. */
(function tagesZahlImReiter(){
  const setzen = () => {
    const n = String(new Date().getDate());
    document.querySelectorAll('.tab .tabnum').forEach(t => { if (t.textContent !== n) t.textContent = n; });
  };
  setzen();
  document.addEventListener('visibilitychange', () => { if (!document.hidden) setzen(); });
  setInterval(setzen, 60000);
})();
