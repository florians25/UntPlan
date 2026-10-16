// UntPlanWeb – Einstieg: Sitzung laden, Anmeldung/Einrichtung, Hülle im Stil des Vorbilds (linke Navigationsleiste mit
// Untermenüs, Schuljahr-Pille, Benutzer unten, Konto wechseln) und Hash-Router.
import { LOGO, icon, e, holen, senden, api, fehler, alarm, lade, initialen, toast } from './ui.js';

const ANSICHTEN = {
  heute: () => import('./ansichten/heute.js'),
  uebersicht: () => import('./ansichten/uebersicht.js'),
  plan: () => import('./ansichten/plan.js'),
  tag: () => import('./ansichten/tag.js'),
  vertretungen: () => import('./ansichten/vertretungen.js'),
  mitteilungen: () => import('./ansichten/mitteilungen.js'),
  unterricht: () => import('./ansichten/unterricht.js'),
  offen: () => import('./ansichten/offen.js'),
  pruefungen: () => import('./ansichten/pruefungen.js'),
  pruefungskalender: () => import('./ansichten/pruefungskalender.js'),
  schueler: () => import('./ansichten/schueler.js'),
  aufgaben: () => import('./ansichten/hausaufgaben.js'),
  klassenbuch: () => import('./ansichten/klassenbuch.js'),
  eintraege: () => import('./ansichten/eintraege.js'),
  hausaufgaben: () => import('./ansichten/hausaufgaben.js'),
  dienste: () => import('./ansichten/dienste.js'),
  abwesenheiten: () => import('./ansichten/abwesenheiten.js'),
  befreiungen: () => import('./ansichten/befreiungen.js'),
  berichte: () => import('./ansichten/berichte.js'),
  sitzplan: () => import('./ansichten/sitzplan.js'),
  melden: () => import('./ansichten/melden.js'),
  stammdaten: () => import('./ansichten/stammdaten.js'),
  admin: () => import('./ansichten/admin.js'),
  profil: () => import('./ansichten/profil.js'),
};

const app = document.getElementById('app');
let sitzung = null;
let elementeCache = null;
let laufendeAnsicht = null;
/** Geöffnetes Untermenü (Gruppen-ID) oder null für das Hauptmenü. */
let untermenue = null;
let aktiverPunkt = null;

/** Gemeinsamer Kontext für alle Ansichten. */
const kontext = {
  get sitzung() { return sitzung; },
  get ich() { return sitzung?.benutzer; },
  /**
   * Gewähltes Schuljahr (Schuljahr-Pille in der Navigation): { von, bis, text, aktuell }. Listen und Berichte nehmen es als Zeitraum;
   * ohne Auswahl gilt das aktuelle Schuljahr aus UntPlan.
   */
  get schuljahr() { return gewaehltesSchuljahr(); },
  /** Ansichtseinstellungen der Administration (Wochenende, Uhrzeiten auf Karten …). */
  get ansicht() { return sitzung?.ansicht ?? {}; },
  /** Stammdaten (Klassen, Lehrkräfte, Räume, Fächer, Zeitraster) – einmal je Sitzung geladen. */
  async elemente(neu = false) {
    if (!elementeCache || neu) elementeCache = await holen('/api/app/elemente');
    return elementeCache;
  },
  /** Seitentitel (mit Zusatz wie „6 / 14“) und Aktionen rechts im Seitenkopf. Ohne Titel und Aktionen entfällt der Kopf. */
  titel(text, rechts = '', zusatz = '') {
    const kopf = document.getElementById('seitenkopf');
    const h = document.getElementById('titel');
    if (h) h.innerHTML = text ? `${e(text)}${zusatz ? ` <small>${e(zusatz)}</small>` : ''}` : '';
    const k = document.getElementById('kopftitel');
    if (k) k.textContent = text || 'UntPlanWeb';
    const r = document.getElementById('aktionen');
    if (r) r.innerHTML = rechts;
    kopf?.classList.toggle('d-none', !text && !rechts);
    document.title = text ? `${text} – UntPlanWeb` : 'UntPlanWeb';
  },
  /** Weiße Werkzeugleiste über dem Inhalt (z. B. Suche und Zeitraum im Klassenbuch); leer = keine Leiste. */
  werkzeug(html = '') {
    const w = document.getElementById('werkzeug');
    if (!w) return null;
    w.innerHTML = html;
    w.classList.toggle('d-none', !html);
    document.querySelector('.u-app')?.classList.toggle('u-mit-werkzeug', !!html);
    return w;
  },
  navigieren(hash, ersetzen = false) {
    if (ersetzen) { history.replaceState(null, '', hash); route(); }
    else if (location.hash === hash) route();
    else location.hash = hash;
  },
  async sitzungNeu() {
    sitzung = await holen('/api/app/sitzung');
    zaehlerAktualisieren();
    return sitzung;
  },
  fehler,
};

window.addEventListener('untplan:abgemeldet', () => { if (sitzung?.benutzer) { sitzung.benutzer = null; start(); } });
window.addEventListener('hashchange', () => route());

// Einmalanmeldung für UntPlanMobile: nach der Anmeldung zurück zu /sso/… (nur relative Pfade, kein offener Weiterleiter)
const weiterNachAnmeldung = (() => {
  const w = new URLSearchParams(location.search).get('weiter');
  if (w && w.startsWith('/sso/') && !w.startsWith('//')) {
    try { sessionStorage.setItem('untplan:weiter', w); } catch { /* privat */ }
    history.replaceState(null, '', location.pathname + location.hash);
  }
  try { return sessionStorage.getItem('untplan:weiter'); } catch { return w; }
})();

async function start() {
  try { sitzung = await holen('/api/app/sitzung'); }
  catch (err) {
    app.innerHTML = `<main class="anmeldung"><div class="karte">${alarm('danger', err.message)}<button class="btn btn-primary w-100" data-neu>Erneut versuchen</button></div></main>`;
    app.querySelector('[data-neu]').onclick = () => location.reload();
    return;
  }
  elementeCache = null;
  if (!sitzung.eingerichtet) return einrichtung();
  if (!sitzung.benutzer) return anmeldung();
  if (sitzung.benutzer.passwortAendern) return passwortPflicht();
  if (weiterNachAnmeldung) {
    try { sessionStorage.removeItem('untplan:weiter'); } catch { /* privat */ }
    location.href = weiterNachAnmeldung;
    return;
  }
  huelle();
  route();
}

// ------------------------------------------------------------------ Anmeldung und Einrichtung

function anmeldeRahmen(titel, inhalt) {
  app.innerHTML = `<main class="anmeldung"><div class="karte">
    <div class="kopf">${LOGO}<b>UntPlanWeb</b><small>${e(sitzung?.schule?.name || 'Stundenplan, Vertretungsplan und Klassenbuch')}</small></div>
    <h1>${e(titel)}</h1>
    ${inhalt}
    <div class="fuss">UntPlanWeb · Webbegleiter zu UntPlan</div></div></main>`;
  return app.querySelector('form');
}

const meldung = (form, text) => { form.querySelector('[data-meldung]').innerHTML = alarm('danger', text); };

function anmeldung() {
  document.title = 'Anmelden – UntPlanWeb';
  const form = anmeldeRahmen('Anmelden', `<form autocomplete="on">
      <div class="mb-3"><label class="form-label" for="login">Benutzername</label><input class="form-control" id="login" name="login" autocomplete="username" required autofocus></div>
      <div class="mb-3"><label class="form-label" for="passwort">Passwort</label><input class="form-control" id="passwort" name="passwort" type="password" autocomplete="current-password" required></div>
      <div data-meldung></div>
      <button class="btn btn-primary w-100 mt-2" type="submit">Login</button>
    </form>`);
  form.onsubmit = async ev => {
    ev.preventDefault();
    const btn = form.querySelector('button');
    btn.disabled = true;
    try {
      await senden('/api/app/anmelden', { login: form.login.value.trim(), passwort: form.passwort.value });
      await start();
    } catch (err) { meldung(form, err.message); form.passwort.select(); }
    finally { btn.disabled = false; }
  };
}

function einrichtung() {
  document.title = 'Einrichtung – UntPlanWeb';
  const form = anmeldeRahmen('Ersteinrichtung', `<form>
      <p class="small text-body-secondary text-center">Legen Sie die Schule und das Administrationskonto an.</p>
      <div class="mb-3"><label class="form-label">Name der Schule</label><input class="form-control" name="schulname" required autofocus></div>
      <div class="mb-3"><label class="form-label">Benutzername</label><input class="form-control" name="login" value="admin" required autocomplete="username"></div>
      <div class="mb-3"><label class="form-label">Passwort</label><input class="form-control" name="passwort" type="password" minlength="8" required autocomplete="new-password"><div class="form-text">Mindestens 8 Zeichen.</div></div>
      <div class="mb-3"><label class="form-label">Passwort wiederholen</label><input class="form-control" name="passwort2" type="password" minlength="8" required autocomplete="new-password"></div>
      <div data-meldung></div>
      <button class="btn btn-primary w-100" type="submit">Einrichten</button>
    </form>`);
  form.onsubmit = async ev => {
    ev.preventDefault();
    if (form.passwort.value !== form.passwort2.value) return meldung(form, 'Die Passwörter stimmen nicht überein.');
    try {
      const r = await senden('/api/app/einrichten', { schulname: form.schulname.value.trim(), login: form.login.value.trim(), passwort: form.passwort.value });
      try { sessionStorage.setItem('untplan:neuerSchluessel', JSON.stringify({ schulId: r.schulId, apiSchluessel: r.apiSchluessel })); } catch { /* privat */ }
      location.hash = '#/admin/sync';
      await start();
    } catch (err) { meldung(form, err.message); }
  };
}

function passwortPflicht() {
  document.title = 'Passwort ändern – UntPlanWeb';
  const form = anmeldeRahmen('Neues Passwort festlegen', `<form>
      <p class="small text-body-secondary text-center">Bitte vergeben Sie vor der ersten Nutzung ein eigenes Passwort.</p>
      <div class="mb-3"><label class="form-label">Bisheriges Passwort</label><input class="form-control" name="alt" type="password" required autocomplete="current-password" autofocus></div>
      <div class="mb-3"><label class="form-label">Neues Passwort</label><input class="form-control" name="neu" type="password" minlength="8" required autocomplete="new-password"><div class="form-text">Mindestens 8 Zeichen.</div></div>
      <div class="mb-3"><label class="form-label">Neues Passwort wiederholen</label><input class="form-control" name="neu2" type="password" minlength="8" required autocomplete="new-password"></div>
      <div data-meldung></div>
      <button class="btn btn-primary w-100 mb-2" type="submit">Speichern</button>
      <button class="btn btn-link w-100 btn-sm" type="button" data-abmelden>Abmelden</button>
    </form>`);
  form.querySelector('[data-abmelden]').onclick = abmelden;
  form.onsubmit = async ev => {
    ev.preventDefault();
    if (form.neu.value !== form.neu2.value) return meldung(form, 'Die Passwörter stimmen nicht überein.');
    try { await senden('/api/app/passwort', { alt: form.alt.value, neu: form.neu.value }); await start(); }
    catch (err) { meldung(form, err.message); }
  };
}

async function abmelden() {
  try { await senden('/api/app/abmelden', {}); } catch { /* egal */ }
  sitzung = null;
  history.replaceState(null, '', '#/heute');
  start();
}

// ------------------------------------------------------------------ Menü (Aufbau wie im Original)

/** Haupt- und Untermenüpunkte je nach Rolle. Ein Punkt hat nav (Schlüssel für „aktiv“), href, text, icon. */
function menue() {
  const b = sitzung.benutzer;
  const k = b.kollegium;
  const m = [
    { id: 'heute', nav: 'heute', href: '#/heute', text: 'Heute', icon: 'dashboard' },
    { id: 'uebersicht', nav: 'uebersicht', href: '#/uebersicht', text: 'Übersicht', icon: 'uebersicht' },
    { id: 'mitteilungen', text: 'Mitteilungen', icon: 'mitteilungen', zaehler: true, unter: [
      { nav: 'mitteilungen', href: '#/mitteilungen', text: 'Posteingang', icon: 'posteingang', zaehler: true },
      { nav: 'mitteilungen-gesendet', href: '#/mitteilungen/gesendet', text: 'Gesendet', icon: 'gesendet' },
      { nav: 'mitteilungen-entwuerfe', href: '#/mitteilungen/entwuerfe', text: 'Entwürfe', icon: 'entwuerfe' },
      ...(k ? [{ nav: 'mitteilungen-listen', href: '#/mitteilungen/listen', text: 'Listen', icon: 'listen' }] : []),
    ] },
  ];
  const plaene = [];
  if (b.element) plaene.push({ nav: 'plan-mein', href: '#/plan/mein', text: 'Mein Stundenplan', icon: 'meinplan' });
  plaene.push({ nav: 'plan-klasse', href: '#/plan/klasse', text: 'Klasse', icon: 'klasse' });
  if (k) plaene.push({ nav: 'plan-lehrer', href: '#/plan/lehrer', text: 'Lehrkraft', icon: 'lehrkraft' });
  plaene.push({ nav: 'plan-raum', href: '#/plan/raum', text: 'Raum', icon: 'raumplan' });
  if (k) {
    plaene.push({ nav: 'tag-klasse', href: '#/tag/klasse', text: 'Tag - Klassen', icon: 'tagklassen' });
    plaene.push({ nav: 'tag-lehrer', href: '#/tag/lehrer', text: 'Tag - Lehrkräfte', icon: 'taglehrer' });
    plaene.push({ nav: 'tag-raum', href: '#/tag/raum', text: 'Tag - Räume', icon: 'tagraeume' });
    plaene.push({ nav: 'vertretungen', href: '#/vertretungen', text: 'Vertretungsplan', icon: 'vertretungen' });
    m.push({ id: 'stundenplan', text: 'Stundenplan', icon: 'stundenplan', unter: plaene });
    // Unterricht wie in der Vorlage: Mein Unterricht, Klasse, Schüler*in, Prüfungen, Prüfungskalender, Aufgaben, Lehrkraft - Tag, Klasse - Tag
    m.push({ id: 'unterricht', text: 'Unterricht', icon: 'unterricht', unter: [
      { nav: 'unterricht', href: '#/unterricht', text: b.element ? 'Mein Unterricht' : 'Unterricht', icon: 'meineunterrichte' },
      { nav: 'unterricht-klasse', href: '#/unterricht/klasse', text: 'Klasse', icon: 'klasse' },
      { nav: 'schueler', href: '#/schueler', text: 'Schüler*in', icon: 'benutzer' },
      { nav: 'pruefungen', href: '#/pruefungen', text: 'Prüfungen', icon: 'klausur' },
      { nav: 'pruefungskalender', href: '#/pruefungskalender', text: 'Prüfungskalender', icon: 'pruefung' },
      { nav: 'aufgaben', href: '#/aufgaben', text: 'Aufgaben', icon: 'vertretungen' },
      { nav: 'u-tag-lehrer', href: '#/tag/lehrer/-/u', text: 'Lehrkraft - Tag', icon: 'taglehrer' },
      { nav: 'u-tag-klasse', href: '#/tag/klasse/-/u', text: 'Klasse - Tag', icon: 'tagklassen' },
    ] });
    m.push({ id: 'klassenbuch', text: 'Klassenbuch', icon: 'klassenbuchnav', unter: [
      { nav: 'klassenbuch', href: '#/klassenbuch', text: 'Übersicht', icon: 'uebersicht' },
      { nav: 'offen', href: '#/offen', text: 'Offene Stunden', icon: 'offen' },
      { nav: 'eintraege', href: '#/eintraege', text: 'Klassenbucheinträge', icon: 'eintraege' },
      { nav: 'hausaufgaben', href: '#/hausaufgaben', text: 'Hausaufgaben', icon: 'hausaufgaben' },
      { nav: 'dienste', href: '#/dienste', text: 'Klassendienste', icon: 'klassendienste' },
      { nav: 'berichte', href: '#/berichte', text: 'Berichte', icon: 'berichte' },
      { nav: 'klassenbuch-woche', href: '#/klassenbuch/-/woche', text: 'Wochenübersicht', icon: 'woche' },
      ...(b.fehlzeiten ? [{ nav: 'abwesenheiten', href: '#/abwesenheiten', text: 'Abwesenheiten', icon: 'abwesenheiten' },
        { nav: 'fehlzeiten', href: '#/klassenbuch/-/fehlzeiten', text: 'Fehlzeiten', icon: 'fehlzeiten' }] : []),
      { nav: 'befreiungen', href: '#/befreiungen', text: 'Befreiungen', icon: 'befreiungen' },
      { nav: 'sitzplan', href: '#/sitzplan', text: 'Sitzplan', icon: 'sitzplan' },
    ] });
    m.push({ id: 'melden', text: 'Melden & Buchen', icon: 'meldenbuchen', unter: [
      { nav: 'melden-abwesenheit', href: '#/melden/abwesenheit', text: 'Abwesenheit melden', icon: 'abwesend' },
      { nav: 'melden-raum', href: '#/melden/raum', text: 'Raum buchen', icon: 'raum' },
      { nav: 'melden-klausur', href: '#/melden/klausur', text: 'Prüfung eintragen', icon: 'klausur' },
      { nav: 'melden-meine', href: '#/melden/meine', text: 'Meine Meldungen', icon: 'protokoll' },
    ] });
  } else {
    if (b.element) m.push({ id: 'plan-mein', nav: 'plan-mein', href: '#/plan/mein', text: 'Mein Stundenplan', icon: 'meinplan' });
    m.push({ id: 'weitere', text: 'Weitere Stundenpläne', icon: 'stundenplan', unter: plaene.filter(p => p.nav !== 'plan-mein') });
    m.push({ id: 'vertretungen', nav: 'vertretungen', href: '#/vertretungen', text: 'Vertretungsplan', icon: 'vertretungen' });
    m.push({ id: 'hausaufgaben', nav: 'hausaufgaben', href: '#/hausaufgaben', text: 'Hausaufgaben', icon: 'hausaufgaben' });
    m.push({ id: 'pruefungen', nav: 'pruefungen', href: '#/pruefungen', text: 'Prüfungen', icon: 'pruefung' });
  }
  if (b.admin) {
    m.push({ id: 'stammdaten', text: 'Stammdaten', icon: 'stammdaten', unter: [
      { nav: 'stammdaten-klassen', href: '#/stammdaten/klassen', text: 'Klassen', icon: 'klasse' },
      { nav: 'stammdaten-schueler', href: '#/stammdaten/schueler', text: 'Schüler*innen', icon: 'benutzer' },
      { nav: 'stammdaten-lehrer', href: '#/stammdaten/lehrer', text: 'Lehrkräfte', icon: 'lehrkraft' },
      { nav: 'stammdaten-faecher', href: '#/stammdaten/faecher', text: 'Fächer', icon: 'klausur' },
      { nav: 'stammdaten-raeume', href: '#/stammdaten/raeume', text: 'Räume', icon: 'raumplan' },
      { nav: 'stammdaten-versionen', href: '#/stammdaten/versionen', text: 'Stundenplan-Versionen', icon: 'versionen' },
      { nav: 'stammdaten-gruende', href: '#/stammdaten/gruende', text: 'Abwesenheitsgründe', icon: 'abwesenheiten' },
      { nav: 'stammdaten-dienste', href: '#/stammdaten/dienste', text: 'Dienste', icon: 'klassendienste' },
    ] });
    m.push({ id: 'admin', text: 'Administration', icon: 'verwaltung', unter: [
      { nav: 'admin-sync', href: '#/admin/sync', text: 'Übersicht & Synchronisation', icon: 'synchron' },
      { nav: 'admin-nachrichten', href: '#/admin/nachrichten', text: 'Nachrichten zum Tag', icon: 'nachrichten' },
      { nav: 'admin-freietage', href: '#/admin/freietage', text: 'Freie Tage & Ferien', icon: 'freietage' },
      { nav: 'admin-import', href: '#/admin/import', text: 'Import', icon: 'import' },
      { nav: 'admin-export', href: '#/admin/export', text: 'Export', icon: 'export' },
      { nav: 'admin-aktive', href: '#/admin/aktive', text: 'Aktive Benutzer', icon: 'aktive' },
      { nav: 'admin-rechte', href: '#/admin/rechte', text: 'Rechte und Rollen', icon: 'rechte' },
      { nav: 'admin-benutzer', href: '#/admin/benutzer', text: 'Benutzer', icon: 'benutzer' },
      { nav: 'admin-zuordnung', href: '#/admin/zuordnung', text: 'Zuordnungsgruppen', icon: 'zuordnung' },
      { nav: 'admin-rueckmeldungen', href: '#/admin/rueckmeldungen', text: 'Rückmeldungen', icon: 'ruckmeldung' },
      { nav: 'admin-sperren', href: '#/admin/sperren', text: 'Prüfungssperren', icon: 'sperre' },
      { nav: 'admin-verspaetung', href: '#/admin/verspaetung', text: 'Verspätungen & Fehlzeiten', icon: 'abwesenheiten' },
      { nav: 'admin-ansicht', href: '#/admin/ansicht', text: 'Ansichtseinstellungen', icon: 'ansicht' },
      { nav: 'admin-monitore', href: '#/admin/monitore', text: 'Monitoransichten', icon: 'monitor' },
      { nav: 'admin-protokoll', href: '#/admin/protokoll', text: 'Protokoll', icon: 'protokoll' },
    ] });
  } else if (b.nachrichten) m.push({ id: 'admin', nav: 'admin-nachrichten', href: '#/admin/nachrichten', text: 'Nachrichten zum Tag', icon: 'nachrichten' });
  return m;
}

const allePunkte = () => menue().flatMap(g => g.unter ? g.unter : [g]);

/** Schlüssel des aktiven Menüpunkts aus der Adresse. */
function navAus(teile) {
  const b = sitzung.benutzer;
  const [name, a, c] = teile;
  switch (name) {
    case 'plan': return !a || a === 'mein' || (a === b.typ && b.element && (c ?? b.element).toLowerCase() === b.element.toLowerCase()) ? 'plan-mein' : 'plan-' + a;
    case 'tag': return (teile[3] === 'u' ? 'u-tag-' : 'tag-') + (a || 'klasse');
    case 'unterricht': return a === 'klasse' ? 'unterricht-klasse' : 'unterricht';
    case 'mitteilungen': return a === 'gesendet' || a === 'entwuerfe' || a === 'listen' ? 'mitteilungen-' + a : 'mitteilungen';
    case 'klassenbuch': return c === 'fehlzeiten' ? 'fehlzeiten' : c && c !== 'uebersicht' ? 'klassenbuch-woche' : 'klassenbuch';
    case 'melden': return 'melden-' + (a || 'abwesenheit');
    case 'stammdaten': return 'stammdaten-' + (a || 'klassen');
    case 'admin': return 'admin-' + (a || (b.admin ? 'sync' : 'nachrichten'));
    default: return name || 'heute';
  }
}

function punktHtml(p) {
  const attr = p.unter ? `data-gruppe="${p.id}"` : `data-nav="${p.nav}"`;
  return `<a class="u-nav-punkt" href="${p.unter ? p.unter.find(x => x.href)?.href : p.href}" ${attr} title="${e(p.text)}"><span class="u-nav-sym">${icon(p.icon)}</span><span class="u-nav-text">${e(p.text)}</span>`
    + `${p.zaehler ? '<span class="u-zahl d-none" data-zaehler></span>' : ''}</a>`;
}

/** Schuljahre zur Auswahl: das aktuelle aus UntPlan und die sieben vorherigen (1.8. – 31.7.). */
function schuljahre() {
  const sj = sitzung?.schuljahr;
  if (!sj) return [];
  const start = Number(sj.von.slice(0, 4));
  return Array.from({ length: 8 }, (_, i) => {
    const j = start - i;
    return i === 0 ? { von: sj.von, bis: sj.bis, text: schuljahrText(), aktuell: true } : { von: `${j}-08-01`, bis: `${j + 1}-07-31`, text: `${j}/${j + 1}`, aktuell: false };
  });
}

function gewaehltesSchuljahr() {
  const liste = schuljahre();
  let von = null;
  try { von = sessionStorage.getItem('untplan:schuljahr'); } catch { /* privat */ }
  return liste.find(x => x.von === von) ?? liste[0] ?? { von: '', bis: '', text: '', aktuell: true };
}

function schuljahrText() {
  const sj = sitzung.schuljahr;
  if (!sj) return '';
  const v = sj.von.slice(0, 4), b = sj.bis.slice(0, 4);
  return v === b ? v : `${v}/${b}`;
}

/** Navigationsleiste neu zeichnen (Haupt- oder Untermenü). */
function leisteZeichnen() {
  const leiste = document.getElementById('seitenleiste');
  if (!leiste) return;
  const gruppen = menue();
  const gruppe = gruppen.find(g => g.id === untermenue && g.unter);
  const jahr = schuljahrText();
  leiste.classList.toggle('mit-unter', !!gruppe);
  leiste.querySelector('[data-leistenkopf]').innerHTML = gruppe
    ? `<div class="u-unterkopf"><button type="button" class="u-zurueck" data-zurueck title="Zurück" aria-label="Zurück">${icon('zurueck')}</button><b title="${e(gruppe.text)}">${e(gruppe.text)}</b></div>`
    : `<a class="u-marke" href="#/heute" title="UntPlanWeb">${LOGO}<span><b>UntPlanWeb</b><small>${e(sitzung.schule.name || '')}</small></span></a>`;
  const gw = gewaehltesSchuljahr();
  leiste.querySelector('[data-jahr]').innerHTML = jahr ? `<span class="u-jahrwahl">
      <button type="button" class="u-jahr mit-pfeil ${gw.aktuell ? '' : 'anders'}" data-jahrwahl aria-haspopup="listbox" aria-expanded="false" aria-describedby="u-jahr-tipp">${e(gw.text)} <i class="bi bi-chevron-down"></i></button>
      <span class="u-jahr-tipp" id="u-jahr-tipp" role="tooltip">Aktuelles Schuljahr: ${e(jahr)}</span>
      <span class="u-jahrliste d-none" role="listbox" data-jahrliste>${schuljahre().map(x => `<button type="button" role="option" data-jahr-von="${x.von}" aria-selected="${x.text === gw.text}" class="${x.text === gw.text ? 'an' : ''}">${e(x.text)}</button>`).join('')}</span></span>` : '';
  const jw = leiste.querySelector('[data-jahrwahl]');
  if (jw) {
    const liste = leiste.querySelector('[data-jahrliste]');
    jw.onclick = ev => { ev.stopPropagation(); liste.classList.toggle('d-none'); jw.setAttribute('aria-expanded', String(!liste.classList.contains('d-none'))); };
    liste.querySelectorAll('[data-jahr-von]').forEach(b => b.onclick = () => {
      try { sessionStorage.setItem('untplan:schuljahr', b.dataset.jahrVon); } catch { /* privat */ }
      liste.classList.add('d-none');
      leisteZeichnen();
      toast(`Schuljahr ${b.textContent} gewählt.`);
      route();
    });
    document.addEventListener('click', () => liste.classList.add('d-none'), { once: true });
  }
  const nav = leiste.querySelector('nav');
  nav.className = `u-nav ${gruppe ? 'u-nav-unter' : ''}`;
  nav.innerHTML = (gruppe ? gruppe.unter : gruppen).map(punktHtml).join('');
  aktivMarkieren();
  zaehlerAktualisieren();
}

function aktivMarkieren() {
  const gruppen = menue();
  const gruppe = gruppen.find(g => g.unter?.some(p => p.nav === aktiverPunkt));
  document.querySelectorAll('#seitenleiste [data-nav]').forEach(a => a.classList.toggle('aktiv', a.dataset.nav === aktiverPunkt));
  document.querySelectorAll('#seitenleiste [data-gruppe]').forEach(a => a.classList.toggle('aktiv', a.dataset.gruppe === gruppe?.id));
  document.querySelector('.u-benutzer')?.classList.toggle('aktiv', aktiverPunkt === 'profil');
}

function huelle() {
  const b = sitzung.benutzer;
  let zu = false;
  try { zu = localStorage.getItem('untplan:leiste') === 'zu'; } catch { /* privat */ }
  app.innerHTML = `
    <div class="u-app ${zu ? 'zu' : ''}">
      <header class="u-kopfleiste">
        <button type="button" class="rundknopf" data-menue aria-label="Menü öffnen" aria-controls="seitenleiste">${icon('menue')}</button>
        ${LOGO}<b id="kopftitel">UntPlanWeb</b>
      </header>
      <aside class="u-leiste" id="seitenleiste" aria-label="Navigation">
        <div data-leistenkopf></div>
        <div class="u-jahrzeile" data-jahr></div>
        <nav class="u-nav"></nav>
        <div class="u-fuss">
          <button type="button" class="u-griff" data-einklappen title="Navigation ein-/ausklappen" aria-label="Navigation ein- oder ausklappen"><span></span></button>
          <button type="button" class="u-benutzer" data-kontomenue title="Konto" aria-haspopup="menu" aria-expanded="false"><span class="u-avatar">${e(b.login.slice(0, 1).toUpperCase())}</span>
            <span class="u-benutzer-text"><b>${e(b.login)}</b><small>${e(b.rollenname)}</small></span></button>
          <div class="u-kontomenue d-none" role="menu" data-konten></div>
          <button type="button" class="u-abmelden" data-abmelden title="Abmelden">${icon('abmelden')}<span>Abmelden</span></button>
        </div>
      </aside>
      <div class="u-schleier" data-schleier></div>
      <main class="u-haupt">
        <div class="u-werkzeug d-none nicht-drucken" id="werkzeug"></div>
        <div class="u-inhalt">
          <div class="u-seitenkopf nicht-drucken-titel d-none" id="seitenkopf"><h1 id="titel"></h1><div class="u-aktionen" id="aktionen"></div></div>
          <div id="seite"></div>
        </div>
      </main>
    </div>`;
  const huelleEl = app.querySelector('.u-app');
  document.body.classList.toggle('leiste-zu', zu);
  const leiste = document.getElementById('seitenleiste');
  const mobilZu = () => huelleEl.classList.remove('offen');
  app.querySelector('[data-abmelden]').onclick = abmelden;
  app.querySelector('[data-menue]').onclick = () => huelleEl.classList.add('offen');
  app.querySelector('[data-schleier]').onclick = mobilZu;
  app.querySelector('[data-einklappen]').onclick = () => {
    const z = huelleEl.classList.toggle('zu');
    document.body.classList.toggle('leiste-zu', z);
    try { localStorage.setItem('untplan:leiste', z ? 'zu' : 'offen'); } catch { /* privat */ }
  };
  app.querySelector('[data-kontomenue]').onclick = ev => { ev.stopPropagation(); kontomenue(); };
  leiste.addEventListener('click', ev => {
    if (ev.target.closest('[data-zurueck]')) { untermenue = null; leisteZeichnen(); return; }
    const g = ev.target.closest('[data-gruppe]');
    if (g) {
      // Gruppe öffnet ihr Untermenü und zeigt (wie im Original) gleich die erste Seite; auf dem Telefon bleibt das Menü offen
      untermenue = g.dataset.gruppe;
      leisteZeichnen();
      return;
    }
    if (ev.target.closest('[data-nav], .u-marke, [data-profil]')) mobilZu();
  });
  document.addEventListener('keydown', ev => { if (ev.key === 'Escape') { mobilZu(); kontomenueZu(); } });
  document.addEventListener('click', ev => { if (!ev.target.closest('[data-konten]')) kontomenueZu(); });
  leisteZeichnen();
}

// ------------------------------------------------------------------ Konto: Profil, Konto wechseln, weiteres Konto

function kontomenueZu() {
  const m = document.querySelector('[data-konten]');
  if (!m || m.classList.contains('d-none')) return;
  m.classList.add('d-none');
  document.querySelector('[data-kontomenue]')?.setAttribute('aria-expanded', 'false');
}

async function kontomenue() {
  const m = document.querySelector('[data-konten]');
  if (!m.classList.contains('d-none')) return kontomenueZu();
  m.classList.remove('d-none');
  document.querySelector('[data-kontomenue]').setAttribute('aria-expanded', 'true');
  m.innerHTML = `<a href="#/profil" role="menuitem" data-profil>${icon('profil')}<span>Mein Profil</span></a><div class="u-kontoliste">${lade()}</div>`;
  m.querySelector('[data-profil]').onclick = () => kontomenueZu();
  let konten = [];
  try { konten = await holen('/api/app/konten'); } catch { /* still */ }
  const andere = konten.filter(k => !k.aktuell);
  m.querySelector('.u-kontoliste').innerHTML = `${andere.length ? '<div class="u-kontokopf">Konto wechseln</div>' : ''}
    ${andere.map(k => `<div class="u-konto"><button type="button" role="menuitem" data-wechseln="${k.id}"><span class="u-avatar">${e(initialen(k.anzeigename || k.login).slice(0, 1))}</span>
      <span><b>${e(k.login)}</b><small>${e(k.rolle)}</small></span></button><button type="button" class="u-konto-weg" data-vergessen="${k.id}" title="Vom Gerät entfernen" aria-label="Konto vom Gerät entfernen">${icon('x')}</button></div>`).join('')}
    <button type="button" role="menuitem" class="u-konto-neu" data-weiteres>${icon('wechseln')}<span>Weiteres Konto anmelden</span></button>`;
  m.querySelectorAll('[data-wechseln]').forEach(b => b.onclick = async () => {
    try { await senden(`/api/app/konten/${b.dataset.wechseln}/wechseln`, {}); history.replaceState(null, '', '#/heute'); untermenue = null; await start(); toast('Konto gewechselt.'); }
    catch (err) { fehler(err); }
  });
  m.querySelectorAll('[data-vergessen]').forEach(b => b.onclick = async ev => {
    ev.stopPropagation();
    try { await api(`/api/app/konten/${b.dataset.vergessen}`, { methode: 'DELETE' }); b.closest('.u-konto').remove(); } catch (err) { fehler(err); }
  });
  m.querySelector('[data-weiteres]').onclick = abmelden;
}

function zaehlerAktualisieren() {
  if (!sitzung) return;
  document.querySelectorAll('[data-zaehler]').forEach(z => {
    z.textContent = sitzung.ungelesen > 99 ? '99+' : sitzung.ungelesen;
    z.classList.toggle('d-none', !sitzung.ungelesen);
  });
}

// Ungelesene Mitteilungen regelmäßig nachladen
setInterval(async () => {
  if (!sitzung?.benutzer || document.hidden) return;
  try { const s = await holen('/api/app/sitzung'); sitzung.ungelesen = s.ungelesen; zaehlerAktualisieren(); } catch { /* still */ }
}, 120000);

// ------------------------------------------------------------------ Router

async function route() {
  if (!sitzung?.benutzer || sitzung.benutzer.passwortAendern) return;
  // offene Dialoge schließen (z. B. Zurück-Taste im Browser)
  document.querySelectorAll('.modal.show').forEach(m => bootstrap.Modal.getInstance(m)?.hide());
  kontomenueZu();
  const teile = location.hash.replace(/^#\/?/, '').split('/').filter(Boolean).map(decodeURIComponent);
  let name = teile[0] || (kontext.ansicht.startseite === 'stundenplan' && sitzung.benutzer.element ? 'plan' : 'heute');
  let nav = navAus([name, ...teile.slice(1)]);
  // Klassenkonten: frühere Adresse #/klassenbuch zeigt die Hausaufgaben
  if (name === 'klassenbuch' && !sitzung.benutzer.kollegium) { name = 'hausaufgaben'; nav = 'hausaufgaben'; }
  const erlaubt = name === 'profil' || allePunkte().some(p => p.nav === nav);
  if (!ANSICHTEN[name] || !erlaubt) { name = 'heute'; nav = 'heute'; }
  aktiverPunkt = nav;
  // passendes Untermenü öffnen (wie im Original: Stundenplan, Klassenbuch … mit Zurück-Knopf)
  const gruppe = menue().find(g => g.unter?.some(p => p.nav === nav));
  if ((gruppe?.id ?? null) !== untermenue) { untermenue = gruppe?.id ?? null; leisteZeichnen(); }
  else aktivMarkieren();
  const seite = document.getElementById('seite');
  if (!seite) return;
  const nummer = laufendeAnsicht = Symbol(name);
  seite.innerHTML = lade();
  kontext.titel('');
  kontext.werkzeug('');
  document.querySelector('.u-app')?.setAttribute('data-ansicht', name);
  planScrollBeobachten();
  window.scrollTo(0, 0);
  try {
    const modul = await ANSICHTEN[name]();
    if (laufendeAnsicht !== nummer) return;
    seite.innerHTML = '';
    seite.dataset.ansicht = name;
    await modul.zeigen(seite, kontext, teile.slice(1));
  } catch (err) {
    if (laufendeAnsicht !== nummer || err.status === 401) return;
    seite.innerHTML = alarm('danger', err.message);
    if (err.status === undefined) console.error(err);
  }
}

/** Ist diese Ansicht noch die aktuelle? (gegen verspätete Antworten) */
kontext.aktuell = () => { const s = laufendeAnsicht; return () => s === laufendeAnsicht; };

start();


/** Schatten unter dem fixierten Plankopf nur, wenn gescrollt ist (ganz oben kein Schatten). */
function planScrollBeobachten() {
  if (window.__planScroll) return;
  window.__planScroll = true;
  const pruefen = ev => {
    const t = ev?.target;
    const y = t && t !== document && t.scrollTop !== undefined ? t.scrollTop : (window.scrollY || document.documentElement.scrollTop || 0);
    document.body.classList.toggle('plan-gescrollt', y > 2);
  };
  document.addEventListener('scroll', pruefen, { capture: true, passive: true });
  pruefen();
}
