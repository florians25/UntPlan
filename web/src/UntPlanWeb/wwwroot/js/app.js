// UntPlanWeb – Einstieg: Sitzung laden, Anmeldung/Einrichtung, Hülle im Stil des Vorbilds (linke Navigationsleiste mit
// Untermenüs, Schuljahr-Pille, Benutzer unten, Konto wechseln) und Hash-Router.
import { LOGO, icon, e, holen, senden, api, fehler, alarm, lade, initialen, toast } from './ui.js';
import { beobachten as auswahlBeobachten } from './auswahl.js';
import { beobachten as kalenderBeobachten } from './kalender.js';

const ANSICHTEN = {
  heute: () => import('./ansichten/heute.js'),
  uebersicht: () => import('./ansichten/uebersicht.js'),
  plan: () => import('./ansichten/plan.js'),
  tag: () => import('./ansichten/tag.js'),
  vertretungen: () => import('./ansichten/vertretungen.js'),
  mitteilungen: () => import('./ansichten/mitteilungen.js'),
  unterricht: () => import('./ansichten/unterricht.js'),
  unterrichtklassen: () => import('./ansichten/unterrichtklassen.js'),
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
  entschuldigen: () => import('./ansichten/entschuldigungen.js'),
  befreiungen: () => import('./ansichten/befreiungen.js'),
  berichte: () => import('./ansichten/berichte.js'),
  sitzplan: () => import('./ansichten/sitzplan.js'),
  melden: () => import('./ansichten/melden.js'),
  buchen: () => import('./ansichten/buchen.js'),
  stammdaten: () => import('./ansichten/stammdaten.js'),
  admin: () => import('./ansichten/admin.js'),
  profil: () => import('./ansichten/profil.js'),
  neuigkeiten: () => import('./ansichten/neuigkeiten.js'),
  sprechtag: () => import('./ansichten/sprechtag.js'),
  hinweise: () => import('./ansichten/hinweise.js'),
  kbeinstellungen: () => import('./ansichten/kbeinstellungen.js'),
};

const app = document.getElementById('app');
let sitzung = null;
let elementeCache = null;
let laufendeAnsicht = null;
/** Geöffnetes Untermenü (Gruppen-ID) oder null für das Hauptmenü. */
let untermenue = null;
let aktiverPunkt = null;
/** Nächstes Zeichnen bzw. Markieren der Navigation blendet die Menüpunkte gestaffelt ein (nach einem Klick in der Seitenleiste). */
let navEinblendenAngefordert = false;

/** Gemeinsamer Kontext für alle Ansichten. */
const kontext = {
  get sitzung() { return sitzung; },
  get ich() { return sitzung?.benutzer; },
  /** Schüler- bzw. Elternkonto mit eigenen Seiten (Abwesenheiten, Fehlzeiten, Hausaufgaben des Kindes). */
  get familie() { return familie(sitzung?.benutzer); },
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

window.addEventListener('untplan:abgemeldet', () => { if (sitzung?.benutzer) { dialogeSchliessen(); sitzung.benutzer = null; start(); } });

/**
 * Beim Abmelden offene Dialoge (z. B. Stundendetails) schließen: die Dialoge brechen ihre Anfragen und Folgeaktionen ab
 * (Ereignis „untplan:abmeldung“), übrig gebliebene Modals und Hintergründe werden entfernt – die Anmeldeseite bleibt bedienbar.
 */
function dialogeSchliessen() {
  window.dispatchEvent(new CustomEvent('untplan:abmeldung'));
  document.querySelectorAll('.modal').forEach(m => { try { bootstrap.Modal.getInstance(m)?.dispose(); } catch { /* schon weg */ } m.remove(); });
  document.querySelectorAll('.modal-backdrop, .u-hover-label').forEach(x => x.remove());
  document.body.classList.remove('modal-open', 'leiste-zu');
  document.body.style.removeProperty('overflow');
  document.body.style.removeProperty('padding-right');
}
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

/**
 * Anmeldeseite: zweigeteilt. Links eine ruhige, warme Verlaufsfläche mit Bildmarke, Schulname, heutigem Datum und einer
 * zurückhaltenden Illustration (stilisierte Stundenplanwoche); rechts auf hellem Grund eine Formularkarte mit Pill-Knopf.
 * Auf dem Telefon steht ein schmaler Kopf mit Bildmarke und Schulname über der Karte.
 */
function anmeldeRahmen(titel, inhalt, untertitel = '') {
  const heute = new Date().toLocaleDateString('de-DE', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
  const schule = sitzung?.schule?.name || 'UntPlanWeb';
  app.innerHTML = `<main class="anmeldung an-seite">
    <section class="an-buehne" aria-label="${e(schule)}">
      <div class="an-marke">${LOGO}<span>UntPlanWeb</span></div>
      <div class="an-text">
        <p class="an-datum">${e(heute)}</p>
        <h2 class="an-schule">${e(schule)}</h2>
        <p class="an-zeile">Stundenplan, Vertretungen und Klassenbuch – an einem Ort, immer aktuell.</p>
      </div>
      <div class="an-hinweise" data-hinweise hidden></div>
      ${anmeldeBild()}
    </section>
    <section class="an-bereich">
      <div class="an-karte">
        <div class="an-kopf">${LOGO}<div><h1 class="an-titel">${e(titel)}</h1>${untertitel ? `<p class="an-unter">${e(untertitel)}</p>` : ''}</div></div>
        <div class="lg-form">${inhalt}</div>
      </div>
      <footer class="an-fuss"><button type="button" class="lg-app d-none" data-pwa>${icon('herunterladen')}Als App installieren</button>
        <span class="lg-app-text" data-pwa-text>Tipp: Auf dem Smartphone über „Zum Startbildschirm hinzufügen“ wie eine App nutzen.</span>
        <span class="lg-impressum">UntPlanWeb · Webbegleiter zu UntPlan</span></footer>
    </section></main>`;
  // App installieren (PWA), sobald der Browser es anbietet
  const pwa = app.querySelector('[data-pwa]');
  const anbieten = () => { if (!window.__pwaAngebot) return; pwa.classList.remove('d-none'); app.querySelector('[data-pwa-text]')?.classList.add('d-none'); };
  pwa.onclick = async () => { const p = window.__pwaAngebot; if (!p) return; window.__pwaAngebot = null; p.prompt(); try { await p.userChoice; } catch { /* egal */ } pwa.classList.add('d-none'); };
  anbieten();
  window.addEventListener('untplan:pwa', anbieten, { once: true });
  oeffentlicheHinweise(app.querySelector('[data-hinweise]'));
  // Passwortfelder: Auge zum Anzeigen
  app.querySelectorAll('.lg-feld input[type=password]').forEach(i => {
    const b = document.createElement('button');
    b.type = 'button'; b.className = 'lg-auge'; b.setAttribute('aria-label', 'Passwort anzeigen'); b.innerHTML = '<i class="bi bi-eye" aria-hidden="true"></i>';
    b.onclick = () => { const zeigen = i.type === 'password'; i.type = zeigen ? 'text' : 'password'; b.innerHTML = `<i class="bi bi-eye${zeigen ? '-slash' : ''}" aria-hidden="true"></i>`; b.setAttribute('aria-label', zeigen ? 'Passwort verbergen' : 'Passwort anzeigen'); i.focus(); };
    i.after(b);
  });
  return app.querySelector('form');
}
/**
 * Öffentliche Hinweise der Schule (z. B. „Elternsprechtag am …“) dezent auf der Anmeldeseite – ohne Anmeldung, ohne Cookies und ohne Tracking;
 * der Server liefert nur freigegebene, allgemeine Hinweise im Anzeigezeitraum (reiner Text).
 */
async function oeffentlicheHinweise(box) {
  if (!box) return;
  let liste = [];
  try {
    const r = await fetch('/api/app/hinweise/oeffentlich', { credentials: 'omit', cache: 'no-store' });
    if (r.ok) liste = await r.json();
  } catch { return; }
  if (!Array.isArray(liste) || !liste.length || !box.isConnected) return;
  box.innerHTML = `<h2 class="visually-hidden">Hinweise der Schule</h2>${liste.map(h => `<article class="an-hinweis">
      <i class="bi bi-megaphone" aria-hidden="true"></i><div><h3>${e(h.titel)}</h3>${h.text ? `<p>${e(h.text).replace(/\n/g, '<br>')}</p>` : ''}</div></article>`).join('')}`;
  box.hidden = false;
}
/** Ruhige Illustration der Anmeldeseite: stilisierte Stundenplanwoche mit runden Karten (rein dekorativ). */
function anmeldeBild() {
  const farben = ['#f56b2a', '#7fb2d6', '#8ccfa8', '#f3c95c', '#c5b3e6'];
  const spalten = [[0, 1, 3], [0, 2, 3, 4], [1, 2, 4], [0, 1, 2, 4], [0, 3]];
  let karten = '';
  spalten.forEach((reihen, t) => reihen.forEach(r => {
    const f = farben[(t * 2 + r) % farben.length];
    karten += `<rect x="${34 + t * 74}" y="${58 + r * 54}" width="62" height="44" rx="10" fill="#fff" opacity=".92"/>`
      + `<rect x="${40 + t * 74}" y="${64 + r * 54}" width="4" height="32" rx="2" fill="${f}"/>`
      + `<rect x="${50 + t * 74}" y="${68 + r * 54}" width="30" height="5" rx="2.5" fill="#c9d0d6"/><rect x="${50 + t * 74}" y="${79 + r * 54}" width="20" height="4" rx="2" fill="#e1e5e9"/>`;
  }));
  return `<svg class="an-bild" viewBox="0 0 420 340" aria-hidden="true" focusable="false">
      <rect x="14" y="14" width="392" height="312" rx="26" fill="#fff" opacity=".45"/>
      ${[0, 1, 2, 3, 4].map(t => `<rect x="${44 + t * 74}" y="32" width="42" height="8" rx="4" fill="#fff" opacity=".9"/>`).join('')}
      ${[0, 1, 2, 3, 4, 5].map(r => `<line x1="30" x2="390" y1="${54 + r * 54}" y2="${54 + r * 54}" stroke="#fff" stroke-opacity=".7"/>`).join('')}
      ${karten}
      <circle cx="356" cy="290" r="34" fill="#f56b2a" opacity=".12"/><circle cx="356" cy="290" r="12" fill="#f56b2a" opacity=".5"/>
    </svg>`;
}
window.addEventListener('beforeinstallprompt', ev => { ev.preventDefault(); window.__pwaAngebot = ev; window.dispatchEvent(new Event('untplan:pwa')); });

const meldung = (form, text) => { form.querySelector('[data-meldung]').innerHTML = alarm('danger', text); };

function anmeldung() {
  document.title = 'Anmelden – UntPlanWeb';
  const form = anmeldeRahmen('Anmelden', `<form autocomplete="on">
      <label class="lg-feld"><span>Benutzername</span><input id="login" name="login" autocomplete="username" placeholder="z. B. mustermann" required autofocus></label>
      <label class="lg-feld"><span>Passwort</span><input id="passwort" name="passwort" type="password" autocomplete="current-password" placeholder="Passwort eingeben" required></label>
      <div data-meldung></div>
      <button class="btn btn-primary lg-login" type="submit">Anmelden</button>
      <button type="button" class="lg-vergessen" data-vergessen aria-expanded="false">Passwort vergessen?</button>
      <p class="lg-hinweis d-none" data-vergessen-text>Ein neues Passwort vergibt die Administration Ihrer Schule. Bitte wenden Sie sich dorthin.</p>
    </form>`, 'Mit Ihrem Schulkonto weiter zu Stundenplan und Klassenbuch.');
  // Zugriff gesperrt (Wartungsmodus): Hinweis der Administration über dem Formular
  holen('/api/app/zugriff').then(z => {
    if (z.gesperrt) form.querySelector('[data-meldung]').insertAdjacentHTML('beforebegin', `<div class="lg-wartung" role="status"><i class="bi bi-lock" aria-hidden="true"></i><span>${e(z.hinweis)}</span></div>`);
  }).catch(() => { /* ohne Hinweis */ });
  form.querySelector('[data-vergessen]').onclick = ev => {
    const t = form.querySelector('[data-vergessen-text]');
    t.classList.toggle('d-none');
    ev.currentTarget.setAttribute('aria-expanded', String(!t.classList.contains('d-none')));
  };
  form.onsubmit = async ev => {
    ev.preventDefault();
    const btn = form.querySelector('button[type=submit]');
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
  dialogeSchliessen();
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
      ...(b.rolle === 'admin' ? [{ nav: 'mitteilungen-einstellungen', href: '#/mitteilungen/einstellungen', text: 'Einstellungen', icon: 'verwaltung' }] : []),
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
      { nav: 'buchen-unterricht', href: '#/buchen/unterricht', text: 'Neuer Unterricht', icon: 'meineunterrichte' },
      { nav: 'unterricht-klasse', href: '#/unterricht/klasse', text: 'Klasse', icon: 'klasse' },
      // Unterricht Klassen (unterrichtklassen.js): Schülergruppen, Abmeldungen, befristeter Unterrichtswechsel
      ...(b.rechte?.['unterricht.klassen'] ? [{ nav: 'unterrichtklassen', href: '#/unterrichtklassen', text: 'Unterricht Klassen', icon: 'benutzer' }] : []),
      { nav: 'schueler', href: '#/schueler', text: 'Schüler*in', icon: 'benutzer' },
      { nav: 'pruefungen', href: '#/pruefungen', text: 'Prüfungen', icon: 'klausur' },
      { nav: 'pruefungskalender', href: '#/pruefungskalender', text: 'Prüfungskalender', icon: 'pruefung' },
      { nav: 'aufgaben', href: '#/aufgaben', text: 'Aufgaben', icon: 'vertretungen' },
      { nav: 'u-tag-lehrer', href: '#/tag/lehrer/-/u', text: 'Lehrkraft - Tag', icon: 'taglehrer' },
      { nav: 'u-tag-klasse', href: '#/tag/klasse/-/u', text: 'Klasse - Tag', icon: 'tagklassen' },
      ...(b.admin || b.rechte?.['klassenbuch.einstellungen'] ? [{ nav: 'kbeinstellungen-unterricht', href: '#/kbeinstellungen/unterricht', text: 'Einstellungen', icon: 'verwaltung' }] : []),
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
      // Fachlehrer-Entschuldigungen: Fehlstunden des eigenen Unterrichts entschuldigen
      ...(b.fachlehrerEntschuldigungen ? [{ nav: 'entschuldigen', href: '#/entschuldigen', text: b.fehlzeiten ? 'Fehlzeiten eigener Unterricht' : 'Fehlzeiten', icon: 'fehlzeiten' }] : []),
      { nav: 'befreiungen', href: '#/befreiungen', text: 'Befreiungen', icon: 'befreiungen' },
      { nav: 'sitzplan', href: '#/sitzplan', text: 'Sitzplan', icon: 'sitzplan', verborgen: true },
      ...(b.admin || b.rechte?.['klassenbuch.einstellungen'] ? [{ nav: 'kbeinstellungen', href: '#/kbeinstellungen', text: 'Einstellungen', icon: 'verwaltung' }] : []),
    ] });
    m.push({ id: 'melden', text: 'Melden', icon: 'ruckmeldung', unter: [
      { nav: 'melden-uebersicht', href: '#/melden', text: 'Übersicht', icon: 'ruckmeldung' },
      { nav: 'melden-abwesenheit', href: '#/melden/abwesenheit', text: 'Abwesenheit melden', icon: 'abwesend' },
      { nav: 'melden-klausur', href: '#/melden/klausur', text: 'Prüfung eintragen', icon: 'klausur' },
      { nav: 'melden-meine', href: '#/melden/meine', text: 'Meine Meldungen', icon: 'protokoll' },
    ] });
    // Buchungsmodul (buchen.js): Räume, Ressourcen, Aktivitäten; Verwaltung der Ressourcen und Raumdaten nur für die Administration
    m.push({ id: 'buchen', text: 'Buchen', icon: 'meldenbuchen', unter: [
      { nav: 'buchen-raum', href: '#/buchen/raum', text: 'Raum buchen', icon: 'raum' },
      { nav: 'buchen-ressource', href: '#/buchen/ressource', text: 'Ressource buchen', icon: 'ressource' },
      { nav: 'buchen-aktivitaet', href: '#/buchen/aktivitaet', text: 'Neue Aktivität', icon: 'meineunterrichte' },
      { nav: 'buchen-meine', href: '#/buchen/meine', text: 'Meine Buchungen', icon: 'protokoll' },
      { nav: 'buchen-raeume', href: '#/buchen/raeume', text: 'Raumübersicht', icon: 'tagraeume' },
      { nav: 'buchen-ressourcen', href: '#/buchen/ressourcen', text: 'Ressourcenübersicht', icon: 'woche' },
      ...(b.admin ? [{ nav: 'buchen-verwaltung', href: '#/buchen/verwaltung', text: 'Ressourcen verwalten', icon: 'stammdaten' },
        { nav: 'buchen-raumdaten', href: '#/buchen/raumdaten', text: 'Raumdaten', icon: 'raumplan' }] : []),
    ] });
  } else if (familie(b)) {
    // Schüler*innen und Erziehungsberechtigte wie im Original: Heute, Übersicht, Mitteilungen, Mein Stundenplan, Weitere Stundenpläne,
    // Abwesenheiten, Hausaufgaben (Lehrerpläne gibt es nicht)
    m[1] = { id: 'uebersicht', nav: 'uebersicht', href: '#/uebersicht', text: 'Übersicht', icon: 'meinedaten' };
    // Mitteilungen mit Untermenü nach den Rechten (Mitteilungs-Einstellungen und Rechte-Modell): Posteingang immer; Gesendet und Entwürfe,
    // wenn das Konto schreiben oder antworten darf; Verfassen nur mit Senderecht (an die erlaubten Lehrkräfte/Klassenleitung)
    const mt = b.mitteilungen ?? { senden: false, antworten: false };
    m[2] = { id: 'mitteilungen', text: 'Mitteilungen', icon: 'mitteilungen', zaehler: true, unter: [
      { nav: 'mitteilungen', href: '#/mitteilungen', text: 'Posteingang', icon: 'posteingang', zaehler: true },
      ...(mt.senden || mt.antworten ? [{ nav: 'mitteilungen-gesendet', href: '#/mitteilungen/gesendet', text: 'Gesendet', icon: 'gesendet' },
        { nav: 'mitteilungen-entwuerfe', href: '#/mitteilungen/entwuerfe', text: 'Entwürfe', icon: 'entwuerfe' }] : []),
      ...(mt.senden ? [{ nav: 'mitteilungen-verfassen', href: '#/mitteilungen/verfassen', text: 'Verfassen', icon: 'verfassen' }] : []),
    ] };
    if (b.element) m.push({ id: 'plan-mein', nav: 'plan-mein', href: '#/plan/mein', text: 'Mein Stundenplan', icon: 'meinplan' });
    m.push({ id: 'weitere', text: 'Weitere Stundenpläne', icon: 'stundenplan', unter: plaene.filter(p => p.nav !== 'plan-mein') });
    m.push({ id: 'abwesenheiten', nav: 'abwesenheiten', href: '#/abwesenheiten', text: 'Abwesenheiten', icon: 'abwesenheiten' });
    m.push({ id: 'hausaufgaben', nav: 'hausaufgaben', href: '#/hausaufgaben', text: 'Hausaufgaben', icon: 'hausaufgaben' });
  } else {
    if (b.element) m.push({ id: 'plan-mein', nav: 'plan-mein', href: '#/plan/mein', text: 'Mein Stundenplan', icon: 'meinplan' });
    m.push({ id: 'weitere', text: 'Weitere Stundenpläne', icon: 'stundenplan', unter: plaene.filter(p => p.nav !== 'plan-mein') });
    m.push({ id: 'vertretungen', nav: 'vertretungen', href: '#/vertretungen', text: 'Vertretungsplan', icon: 'vertretungen' });
    m.push({ id: 'hausaufgaben', nav: 'hausaufgaben', href: '#/hausaufgaben', text: 'Hausaufgaben', icon: 'hausaufgaben' });
    m.push({ id: 'pruefungen', nav: 'pruefungen', href: '#/pruefungen', text: 'Prüfungen', icon: 'pruefung' });
  }
  // Sprechtag (sprechtag.js): Lehrkräfte, Schulleitung, Administration, Erziehungsberechtigte und Schüler*innen (nur lesen – Ankündigung, eigene Termine)
  if (k || b.rolle === 'eltern' || b.rolle === 'schueler') m.push({ id: 'sprechtag', nav: 'sprechtag', href: '#/sprechtag', text: 'Sprechtag', icon: 'aktive' });
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
      { nav: 'stammdaten-notenschemen', href: '#/stammdaten/notenschemen', text: 'Notenschemen', icon: 'berichte' },
      { nav: 'stammdaten-pruefungsarten', href: '#/stammdaten/pruefungsarten', text: 'Prüfungsarten', icon: 'klausur' },
    ] });
    m.push({ id: 'admin', text: 'Administration', icon: 'verwaltung', unter: [
      { nav: 'admin-sync', href: '#/admin/sync', text: 'Übersicht & Synchronisation', icon: 'synchron' },
      { nav: 'admin-nachrichten', href: '#/admin/nachrichten', text: 'Nachrichten zum Tag', icon: 'nachrichten' },
      { nav: 'hinweise', href: '#/hinweise', text: 'Hinweise Anmeldeseite', icon: 'info' },
      { nav: 'admin-freietage', href: '#/admin/freietage', text: 'Freie Tage & Ferien', icon: 'freietage' },
      { nav: 'admin-import', href: '#/admin/import', text: 'Import', icon: 'import' },
      { nav: 'admin-export', href: '#/admin/export', text: 'Export', icon: 'export' },
      { nav: 'admin-aktive', href: '#/admin/aktive', text: 'Aktive Benutzer', icon: 'aktive' },
      { nav: 'admin-zugriff', href: '#/admin/zugriff', text: 'Zugriff ent-/sperren', icon: 'sperre' },
      { nav: 'admin-ueber', href: '#/admin/ueber', text: 'Über UntPlan', icon: 'info' },
      { nav: 'admin-rechte', href: '#/admin/rechte', text: 'Rechte und Rollen', icon: 'rechte' },
      { nav: 'admin-benutzer', href: '#/admin/benutzer', text: 'Benutzer', icon: 'benutzer' },
      { nav: 'admin-personen', href: '#/admin/personen', text: 'Personen & Konten', icon: 'lehrkraft' },
      { nav: 'admin-zuordnung', href: '#/admin/zuordnung', text: 'Zuordnungsgruppen', icon: 'zuordnung' },
      { nav: 'admin-rueckmeldungen', href: '#/admin/rueckmeldungen', text: 'Rückmeldungen', icon: 'ruckmeldung' },
      { nav: 'admin-sperren', href: '#/admin/sperren', text: 'Prüfungssperren', icon: 'sperre' },
      { nav: 'admin-verspaetung', href: '#/admin/verspaetung', text: 'Verspätungen & Fehlzeiten', icon: 'abwesenheiten' },
      { nav: 'admin-ansicht', href: '#/admin/ansicht', text: 'Ansichtseinstellungen', icon: 'ansicht' },
      { nav: 'admin-monitore', href: '#/admin/monitore', text: 'Monitoransichten', icon: 'monitor' },
      { nav: 'admin-protokoll', href: '#/admin/protokoll', text: 'Protokoll', icon: 'protokoll' },
    ] });
  } else {
    // Rechte-Modell: einzelne Administrationsbereiche auch ohne Administration (Nachrichten zum Tag, Import, Benutzerverwaltung)
    const r = b.rechte ?? {};
    const unter = [];
    if (b.nachrichten) unter.push({ nav: 'admin-nachrichten', href: '#/admin/nachrichten', text: 'Nachrichten zum Tag', icon: 'nachrichten' });
    if (r['infocenter.verwalten']) unter.push({ nav: 'hinweise', href: '#/hinweise', text: 'Hinweise Anmeldeseite', icon: 'info' });
    if (r['admin.import']) unter.push({ nav: 'admin-import', href: '#/admin/import', text: 'Import', icon: 'import' });
    if (r['admin.wartung']) unter.push({ nav: 'admin-aktive', href: '#/admin/aktive', text: 'Aktive Benutzer', icon: 'aktive' },
      { nav: 'admin-zugriff', href: '#/admin/zugriff', text: 'Zugriff ent-/sperren', icon: 'sperre' }, { nav: 'admin-ueber', href: '#/admin/ueber', text: 'Über UntPlan', icon: 'info' });
    if (r['admin.benutzer']) unter.push({ nav: 'admin-benutzer', href: '#/admin/benutzer', text: 'Benutzer', icon: 'benutzer' }, { nav: 'admin-personen', href: '#/admin/personen', text: 'Personen & Konten', icon: 'lehrkraft' });
    if (unter.length === 1) m.push({ id: 'admin', ...unter[0] });
    else if (unter.length) m.push({ id: 'admin', text: 'Administration', icon: 'verwaltung', unter });
  }
  return m;
}

/** Schüler- bzw. Elternkonto mit zugeordneter Person (eigene Seiten „Meine Daten“); Klassenkonten ohne Zuordnung behalten die Klassenansicht. */
function familie(b) { return !!b?.familie && (b.rolle === 'eltern' || (b.schuelerIds ?? []).length > 0); }

const allePunkte = () => menue().flatMap(g => g.unter ? g.unter : [g]);

/** Schlüssel des aktiven Menüpunkts aus der Adresse. */
function navAus(teile) {
  const b = sitzung.benutzer;
  const [name, a, c] = teile;
  switch (name) {
    // „Mein Stundenplan“ nur über #/plan/mein; unter „Lehrkraft“/„Klasse“ bleibt der Menüpunkt aktiv, auch beim eigenen Element
    case 'plan': return !a || a === 'mein' ? 'plan-mein' : 'plan-' + a;
    case 'tag': return (teile[3] === 'u' ? 'u-tag-' : 'tag-') + (a || 'klasse');
    case 'unterricht': return a === 'klasse' ? 'unterricht-klasse' : 'unterricht';
    case 'mitteilungen': return ['gesendet', 'entwuerfe', 'listen', 'einstellungen', 'verfassen'].includes(a) ? 'mitteilungen-' + a : 'mitteilungen';
    case 'klassenbuch': return c === 'fehlzeiten' ? 'fehlzeiten' : c && c !== 'uebersicht' ? 'klassenbuch-woche' : 'klassenbuch';
    case 'melden': return 'melden-' + (a || 'uebersicht');
    case 'buchen': return a === 'stunden' ? 'buchen-unterricht' : 'buchen-' + (a || 'raum');
    case 'stammdaten': return 'stammdaten-' + (a || 'klassen');
    case 'kbeinstellungen': return a === 'unterricht' ? 'kbeinstellungen-unterricht' : 'kbeinstellungen';
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
  nav.className = `u-nav ${gruppe ? 'u-nav-unter' : ''} ${gruppe && ['admin', 'stammdaten'].includes(gruppe.id) ? 'u-nav-kompakt' : ''}`;
  // verborgene Punkte (z. B. Sitzplan – nur über das Zahnrad in den Stundendetails) bleiben als Route erlaubt, stehen aber nicht im Menü
  nav.innerHTML = (gruppe ? gruppe.unter.filter(p => !p.verborgen) : gruppen).map(punktHtml).join('');
  aktivMarkieren();
  zaehlerAktualisieren();
}

/**
 * Menüpunkte nach einem Klick in der Seitenleiste von oben nach unten gestaffelt einblenden (Deckkraft und 6px Versatz, 220ms, 24ms
 * Abstand, höchstens 14 Stufen). Nur transform/opacity – kein Einfluss auf das Layout; bei „Bewegung reduzieren“ keine Animation.
 */
function navEinblenden() {
  if (!navEinblendenAngefordert) return;
  navEinblendenAngefordert = false;
  const nav = document.querySelector('#seitenleiste nav');
  if (!nav || typeof nav.animate !== 'function') return;
  try { if (matchMedia('(prefers-reduced-motion: reduce)').matches) return; } catch { /* ohne matchMedia */ }
  [...nav.querySelectorAll('.u-nav-punkt')].forEach((p, i) => {
    p.getAnimations?.().forEach(a => a.cancel());
    p.animate([{ opacity: 0, transform: 'translateY(-6px)' }, { opacity: 1, transform: 'none' }],
      { duration: 220, delay: Math.min(i, 14) * 24, easing: 'cubic-bezier(.2, .7, .3, 1)', fill: 'backwards' });
  });
}

function aktivMarkieren() {
  navEinblenden();
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
          <a class="u-abmelden u-neuigkeiten" href="#/neuigkeiten" data-neuigkeiten title="Neuigkeiten">${icon('nachrichten')}<span>Neuigkeiten</span></a>
          <button type="button" class="u-abmelden" data-abmelden title="Abmelden">${icon('abmelden')}<span>Abmelden</span></button>
        </div>
      </aside>
      <div class="u-schleier" data-schleier></div>
      <main class="u-haupt">
        <div class="u-werkzeug d-none nicht-drucken" id="werkzeug"></div>
        <div class="u-inhalt">
          <div class="u-seitenkopf nicht-drucken-titel d-none" id="seitenkopf"><h1 id="titel"></h1><div class="u-aktionen" id="aktionen"></div></div>
          <div class="u-kopfplatz nicht-drucken" id="kopfplatz" aria-hidden="true"></div>
          <div id="seite"></div>
        </div>
      </main>
    </div>`;
  const huelleEl = app.querySelector('.u-app');
  document.body.classList.toggle('leiste-zu', zu);
  const leiste = document.getElementById('seitenleiste');
  const mobilZu = () => huelleEl.classList.remove('offen');
  app.querySelector('[data-abmelden]').onclick = abmelden;
  // Infocenter (Knopf unten rechts): Infos der Schule und Neuigkeiten, Zahl ungelesener Einträge auch am Eintrag „Neuigkeiten“
  import('./infocenter.js').then(m => m.starten(kontext)).catch(() => { /* ohne Infocenter */ });
  app.querySelector('[data-menue]').onclick = () => huelleEl.classList.add('offen');
  app.querySelector('[data-schleier]').onclick = mobilZu;
  app.querySelector('[data-einklappen]').onclick = () => {
    const z = huelleEl.classList.toggle('zu');
    document.body.classList.toggle('leiste-zu', z);
    try { localStorage.setItem('untplan:leiste', z ? 'zu' : 'offen'); } catch { /* privat */ }
  };
  app.querySelector('[data-kontomenue]').onclick = ev => { ev.stopPropagation(); kontomenue(); };
  leiste.addEventListener('click', ev => {
    if (ev.target.closest('[data-zurueck]')) { untermenue = null; navEinblendenAngefordert = true; leisteZeichnen(); return; }
    const g = ev.target.closest('[data-gruppe]');
    if (g) {
      // Gruppe öffnet ihr Untermenü und zeigt (wie im Original) gleich die erste Seite; auf dem Telefon bleibt das Menü offen
      untermenue = g.dataset.gruppe;
      navEinblendenAngefordert = true;
      leisteZeichnen();
      return;
    }
    // Seitenwechsel über einen Menüpunkt: beim nächsten Markieren (route) die Punkte gestaffelt einblenden – nicht beim Klick auf die offene Seite
    const punkt = ev.target.closest('[data-nav]');
    if (punkt && punkt.getAttribute('href') !== location.hash) navEinblendenAngefordert = true;
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
  if (!sitzung?.benutzer || (document.hidden && !sitzung.benutzer.benachrichtigung)) return;
  try {
    const s = await holen('/api/app/sitzung');
    const neu = (s.ungelesen ?? 0) - (sitzung.ungelesen ?? 0);
    sitzung.ungelesen = s.ungelesen;
    zaehlerAktualisieren();
    if (neu > 0) neueMitteilungMelden(neu);
  } catch { /* still */ }
}, 120000);

/** Benachrichtigung über neue Mitteilungen: Hinweis in der App, mit Einwilligung (Profil „Benachrichtigung“) auch als Systemmeldung des Browsers. */
function neueMitteilungMelden(anzahl) {
  const text = anzahl === 1 ? 'Sie haben eine neue Mitteilung empfangen.' : `Sie haben ${anzahl} neue Mitteilungen empfangen.`;
  if (!location.hash.startsWith('#/mitteilungen')) toast(text);
  try {
    if (sitzung.benutzer?.benachrichtigung && 'Notification' in window && Notification.permission === 'granted') {
      const n = new Notification('Neue Mitteilung', { body: text, tag: 'untplan-mitteilung' });
      n.onclick = () => { window.focus(); location.hash = '#/mitteilungen'; n.close(); };
    }
  } catch { /* nicht unterstützt */ }
}

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
  // Sprechtag für Schüler*innen ohne Menüpunkt (Option „sichtbar für Schüler*innen“; Zugang über die Karten auf Heute/Übersicht, der Server prüft)
  const erlaubt = name === 'profil' || name === 'neuigkeiten' || (name === 'sprechtag' && sitzung.benutzer.rolle === 'schueler') || allePunkte().some(p => p.nav === nav);
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
  const kopf = weisserKopf(nav);
  planScrollBeobachten();
  window.scrollTo(0, 0);
  try {
    const modul = await ANSICHTEN[name]();
    if (laufendeAnsicht !== nummer) return;
    seite.innerHTML = '';
    seite.dataset.ansicht = name;
    await modul.zeigen(seite, kontext, teile.slice(1));
    if (laufendeAnsicht === nummer) kopfMerken(kopf);
  } catch (err) {
    if (laufendeAnsicht !== nummer || err.status === 401) return;
    seite.innerHTML = alarm('danger', err.message);
    if (err.status === undefined) console.error(err);
  }
}

/**
 * Klassenbuch-Verwaltungsseiten (Kollegium): weißer Kopf über die volle Breite mit Titel, Aktionen und der Suchleiste
 * (Element mit Klasse u-kopfteil, meist .u-sf-leiste mit suchfeldMitFilter), der Inhalt darunter auf grauem Grund. Damit nichts
 * springt bzw. erst grau und dann weiß erscheint, steht der Kopf sofort beim Seitenwechsel: Titel aus dem Menü, die Zeile der
 * Suchleiste als gleich hoher Platzhalter (#kopfplatz, per CSS sichtbar, solange die Seite noch keinen u-kopfteil gezeichnet hat).
 * Die Höhe der Suchleiste je Seite wird gemerkt (schmale Bildschirme: umbrechende Leisten).
 * suche: Seite mit Suchleiste im Kopf; titel: Titel bis die Seite ihn setzt (sonst der Menütext).
 */
// h: übliche Höhe der Suchleiste (breit ≥ 1300px, mittel, schmal < 768px) für den ersten Aufruf; danach gilt die gemessene Höhe
const WEISSER_KOPF = {
  offen: { suche: true, h: [60, 60, 106] }, hausaufgaben: { suche: true, h: [60, 60, 140] }, pruefungen: { suche: true, h: [60, 60, 106] },
  abwesenheiten: { suche: true, h: [60, 110, 174] }, fehlzeiten: { suche: true, h: [112, 112, 314] }, eintraege: { suche: true, h: [60, 60, 142] },
  dienste: { suche: true, h: [60, 60, 192] }, befreiungen: { suche: true, titel: 'Befreiungen vom Unterricht', h: [60, 60, 142] },
  berichte: { suche: true, h: [80, 134, 200] }, entschuldigen: { suche: true, titel: 'Fehlzeiten', h: [60, 110, 174] },
  kbeinstellungen: { suche: false, titel: 'Klassenbuch-Einstellungen' }, 'kbeinstellungen-unterricht': { suche: false, titel: 'Unterricht-Einstellungen' },
};
const kopfStufe = () => innerWidth >= 1300 ? 0 : innerWidth >= 768 ? 1 : 2;
const kopfSchluessel = nav => `untplan:kopfhoehe:${nav}:${kopfStufe()}`;

function weisserKopf(nav) {
  const u = document.querySelector('.u-app');
  const art = sitzung.benutzer.kollegium ? WEISSER_KOPF[nav] : null;
  u?.classList.toggle('u-kopf-weiss', !!art);
  u?.classList.toggle('u-kopf-suche', !!art?.suche);
  if (!art) return null;
  kontext.titel(art.titel ?? allePunkte().find(p => p.nav === nav)?.text ?? '');
  let h = art.h?.[kopfStufe()];
  try { h = Number(sessionStorage.getItem(kopfSchluessel(nav))) || h; } catch { /* privat */ }
  if (h) u?.style.setProperty('--kopfplatz-h', `${h}px`); else u?.style.removeProperty('--kopfplatz-h');
  return nav;
}

/** Höhe der gezeichneten Suchleiste merken (Platzhalter beim nächsten Aufruf der Seite gleich hoch). */
function kopfMerken(nav) {
  const teil = nav && document.querySelector('#seite .u-kopfteil:not(.u-kopfreiter):not(.u-kopfskelett)');
  if (!teil || teil.offsetHeight <= 0) return;
  try { sessionStorage.setItem(kopfSchluessel(nav), String(Math.round(teil.getBoundingClientRect().height))); } catch { /* privat */ }
}

/** Ist diese Ansicht noch die aktuelle? (gegen verspätete Antworten) */
kontext.aktuell = () => { const s = laufendeAnsicht; return () => s === laufendeAnsicht; };

// Einheitliche Auswahl: alle nativen Selects und Suchfelder werden automatisch ersetzt bzw. vereinheitlicht
auswahlBeobachten(document.body);
// Einheitlicher Kalender: alle Datumsfelder (<input type="date">, "datetime-local") erhalten den eigenen Datumswähler
kalenderBeobachten(document.body);
start();


/**
 * Schatten oben (Kopfleiste auf schmalen Bildschirmen, Werkzeugleiste, Plankopf) nur, wenn nach unten gescrollt ist – bei
 * Scroll-Position 0 kein Schatten. Seiten-Scroll (Fenster) setzt body.gescrollt (bzw. plan-gescrollt); eigene Scroll-Container
 * (Dialoge, [data-kopf-schatten]) erhalten selbst die Klasse „gescrollt“. Andere innere Scrollbereiche
 * (Navigation, Tabellen) ändern nichts mehr am Seitenschatten.
 */
function planScrollBeobachten() {
  if (window.__planScroll) return;
  window.__planScroll = true;
  const seite = () => {
    const unten = (window.scrollY || document.documentElement.scrollTop || 0) > 2;
    document.body.classList.toggle('gescrollt', unten);
    document.body.classList.toggle('plan-gescrollt', unten);
  };
  window.addEventListener('scroll', seite, { passive: true });
  document.addEventListener('scroll', ev => {
    const t = ev.target;
    if (t instanceof Element && t.matches('.modal, [data-kopf-schatten]')) t.classList.toggle('gescrollt', t.scrollTop > 2);
  }, { capture: true, passive: true });
  // Seitenwechsel: scrollTo(0, 0) löst nicht immer ein scroll-Ereignis aus
  window.addEventListener('hashchange', () => requestAnimationFrame(seite));
  seite();
}
