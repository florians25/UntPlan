// UntPlanWeb – Einstieg: Sitzung laden, Anmeldung/Einrichtung, Hülle (Bootstrap) mit Navigation und Hash-Router.
import { LOGO, icon, e, holen, senden, fehler, alarm, lade } from './ui.js';

const ANSICHTEN = {
  heute: () => import('./ansichten/heute.js'),
  plan: () => import('./ansichten/plan.js'),
  tag: () => import('./ansichten/tag.js'),
  vertretungen: () => import('./ansichten/vertretungen.js'),
  mitteilungen: () => import('./ansichten/mitteilungen.js'),
  klassenbuch: () => import('./ansichten/klassenbuch.js'),
  melden: () => import('./ansichten/melden.js'),
  admin: () => import('./ansichten/admin.js'),
  profil: () => import('./ansichten/profil.js'),
};

const app = document.getElementById('app');
let sitzung = null;
let elementeCache = null;
let laufendeAnsicht = null;

/** Gemeinsamer Kontext für alle Ansichten. */
const kontext = {
  get sitzung() { return sitzung; },
  get ich() { return sitzung?.benutzer; },
  /** Stammdaten (Klassen, Lehrkräfte, Räume, Fächer, Zeitraster) – einmal je Sitzung geladen. */
  async elemente(neu = false) {
    if (!elementeCache || neu) elementeCache = await holen('/api/app/elemente');
    return elementeCache;
  },
  /** Seitentitel und Aktionen rechts im Seitenkopf. */
  titel(text, rechts = '') {
    const h = document.getElementById('titel');
    if (h) h.textContent = text;
    const r = document.getElementById('aktionen');
    if (r) r.innerHTML = rechts;
    document.title = text ? `${text} – UntPlanWeb` : 'UntPlanWeb';
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

async function start() {
  try { sitzung = await holen('/api/app/sitzung'); }
  catch (err) {
    app.innerHTML = `<main class="anmeldung"><div class="card shadow-sm"><div class="card-body p-4">
      ${alarm('danger', err.message)}<button class="btn btn-primary w-100" data-neu>Erneut versuchen</button></div></div></main>`;
    app.querySelector('[data-neu]').onclick = () => location.reload();
    return;
  }
  elementeCache = null;
  if (!sitzung.eingerichtet) return einrichtung();
  if (!sitzung.benutzer) return anmeldung();
  if (sitzung.benutzer.passwortAendern) return passwortPflicht();
  huelle();
  route();
}

// ------------------------------------------------------------------ Anmeldung und Einrichtung

function anmeldeRahmen(titel, inhalt) {
  app.innerHTML = `<main class="anmeldung"><div class="card shadow-sm"><div class="card-body p-4">
    <div class="d-flex align-items-center gap-2 mb-1">${LOGO}<span class="fs-5 fw-semibold">UntPlanWeb</span></div>
    <div class="text-body-secondary small mb-4">${e(sitzung?.schule?.name || 'Stundenplan und Vertretungsplan')}</div>
    <h1 class="h6 mb-3">${e(titel)}</h1>
    ${inhalt}</div></div></main>`;
  return app.querySelector('form');
}

const meldung = (form, text) => { form.querySelector('[data-meldung]').innerHTML = alarm('danger', text); };

function anmeldung() {
  document.title = 'Anmelden – UntPlanWeb';
  const form = anmeldeRahmen('Anmelden', `<form autocomplete="on">
      <div class="mb-3"><label class="form-label" for="login">Benutzername</label><input class="form-control" id="login" name="login" autocomplete="username" required autofocus></div>
      <div class="mb-3"><label class="form-label" for="passwort">Passwort</label><input class="form-control" id="passwort" name="passwort" type="password" autocomplete="current-password" required></div>
      <div data-meldung></div>
      <button class="btn btn-primary w-100" type="submit">Anmelden</button>
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
      <p class="small text-body-secondary">Legen Sie die Schule und das Administrationskonto an.</p>
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
      <p class="small text-body-secondary">Bitte vergeben Sie vor der ersten Nutzung ein eigenes Passwort.</p>
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

// ------------------------------------------------------------------ Hülle

function navPunkte() {
  const b = sitzung.benutzer;
  const punkte = [
    { gruppe: 'Pläne' },
    { id: 'heute', text: 'Übersicht', icon: 'heute' },
    { id: 'plan', text: b.kollegium ? 'Stundenpläne' : 'Stundenplan', icon: 'plan' },
  ];
  if (b.kollegium) punkte.push({ id: 'tag', text: 'Tagesübersicht', icon: 'tag' });
  punkte.push({ id: 'vertretungen', text: 'Vertretungsplan', icon: 'vertretung' });
  punkte.push({ gruppe: 'Schule' });
  punkte.push({ id: 'mitteilungen', text: 'Mitteilungen', icon: 'mitteilung', zaehler: true });
  punkte.push({ id: 'klassenbuch', text: b.kollegium ? 'Klassenbuch' : 'Hausaufgaben', icon: 'klassenbuch' });
  if (b.kollegium) punkte.push({ id: 'melden', text: 'Meldungen', icon: 'melden' });
  if (b.admin || b.nachrichten) {
    punkte.push({ gruppe: 'Verwaltung' });
    punkte.push({ id: 'admin', text: b.admin ? 'Administration' : 'Nachrichten zum Tag', icon: 'admin' });
  }
  return punkte;
}

function huelle() {
  const b = sitzung.benutzer;
  app.innerHTML = `
    <nav class="navbar bg-white border-bottom sticky-top px-3 app-navbar">
      <button class="btn btn-link text-body p-0 me-3 d-lg-none" type="button" data-bs-toggle="offcanvas" data-bs-target="#seitenleiste" aria-controls="seitenleiste" aria-label="Menü">${icon('menue', 'fs-4')}</button>
      <a class="navbar-brand d-flex align-items-center gap-2 me-3" href="#/heute">${LOGO}<span class="fw-semibold">UntPlanWeb</span></a>
      <span class="navbar-text small text-body-secondary d-none d-md-inline border-start ps-3 text-truncate" style="max-width:40vw">${e(sitzung.schule.name || '')}</span>
      <div class="ms-auto dropdown">
        <button class="btn btn-link text-body text-decoration-none dropdown-toggle d-flex align-items-center gap-2" type="button" data-bs-toggle="dropdown" aria-expanded="false">
          ${icon('profil', 'fs-5 text-body-secondary')}<span class="d-none d-sm-inline">${e(b.anzeigename || b.login)}</span></button>
        <ul class="dropdown-menu dropdown-menu-end shadow-sm">
          <li><h6 class="dropdown-header">${e(b.rollenname)}${b.element ? ' · ' + e(b.element) : ''}</h6></li>
          <li><a class="dropdown-item" href="#/profil">${icon('profil', 'me-2')}Mein Konto</a></li>
          <li><hr class="dropdown-divider"></li>
          <li><button class="dropdown-item" type="button" data-abmelden>${icon('abmelden', 'me-2')}Abmelden</button></li>
        </ul>
      </div>
    </nav>
    <div class="d-flex app-body">
      <aside class="offcanvas-lg offcanvas-start bg-white border-end app-sidebar" tabindex="-1" id="seitenleiste" aria-label="Navigation">
        <div class="offcanvas-header border-bottom">
          <span class="offcanvas-title fw-semibold">Menü</span>
          <button type="button" class="btn-close" data-bs-dismiss="offcanvas" data-bs-target="#seitenleiste" aria-label="Schließen"></button>
        </div>
        <div class="offcanvas-body d-block p-0 pb-3">
          <nav class="nav flex-column">${navPunkte().map(p => p.gruppe ? `<div class="nav-ueberschrift">${p.gruppe}</div>`
            : `<a class="nav-link" href="#/${p.id}" data-nav="${p.id}">${icon(p.icon)}<span>${p.text}</span>${p.zaehler ? '<span class="badge rounded-pill text-bg-primary ms-auto zaehler d-none" data-zaehler></span>' : ''}</a>`).join('')}</nav>
        </div>
      </aside>
      <main class="flex-grow-1 app-main">
        <div class="container-fluid px-3 px-lg-4 py-3">
          <div class="seitenkopf d-flex flex-wrap align-items-center gap-2 mb-3 nicht-drucken-titel">
            <h1 id="titel"></h1><div class="ms-auto d-flex flex-wrap gap-2" id="aktionen"></div>
          </div>
          <div id="seite"></div>
        </div>
      </main>
    </div>`;
  app.querySelector('[data-abmelden]').onclick = abmelden;
  const leiste = document.getElementById('seitenleiste');
  leiste.addEventListener('click', ev => {
    if (ev.target.closest('.nav-link')) bootstrap.Offcanvas.getInstance(leiste)?.hide();
  });
  zaehlerAktualisieren();
}

function zaehlerAktualisieren() {
  const z = document.querySelector('[data-zaehler]');
  if (!z || !sitzung) return;
  z.textContent = sitzung.ungelesen > 99 ? '99+' : sitzung.ungelesen;
  z.classList.toggle('d-none', !sitzung.ungelesen);
}

// Ungelesene Mitteilungen regelmäßig nachladen
setInterval(async () => {
  if (!sitzung?.benutzer || document.hidden) return;
  try { const s = await holen('/api/app/sitzung'); sitzung.ungelesen = s.ungelesen; zaehlerAktualisieren(); } catch { /* still */ }
}, 120000);

// ------------------------------------------------------------------ Router

async function route() {
  if (!sitzung?.benutzer || sitzung.benutzer.passwortAendern) return;
  const teile = location.hash.replace(/^#\/?/, '').split('/').filter(Boolean).map(decodeURIComponent);
  let name = teile[0] || 'heute';
  if (!ANSICHTEN[name] || (!navPunkte().some(p => p.id === name) && name !== 'profil')) name = 'heute';
  document.querySelectorAll('[data-nav]').forEach(a => a.classList.toggle('active', a.dataset.nav === name));
  const seite = document.getElementById('seite');
  if (!seite) return;
  const nummer = laufendeAnsicht = Symbol(name);
  seite.innerHTML = lade();
  kontext.titel('');
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
