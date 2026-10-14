// UntPlanWeb – Einstieg: Sitzung laden, Anmeldung/Einrichtung, Hülle mit Navigation und Hash-Router.
import { LOGO, icon, e, holen, senden, fehler, initialen } from './ui.js';

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
  titel(text, rechts = '') {
    const h = document.querySelector('.kopf h1');
    if (h) h.textContent = text;
    const r = document.querySelector('.kopf .rechts');
    if (r) r.innerHTML = rechts;
    document.title = `${text} · UntPlanWeb`;
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
};
window.untplan = kontext;

window.addEventListener('untplan:abgemeldet', () => { if (sitzung?.benutzer) { sitzung.benutzer = null; start(); } });
window.addEventListener('hashchange', () => route());

async function start() {
  try { sitzung = await holen('/api/app/sitzung'); }
  catch (err) {
    app.innerHTML = `<div class="anmeldung"><div class="karte"><div class="marke">${LOGO}<div><b>UntPlanWeb</b></div></div>
      <div class="hinweis fehler">${icon('warnung')}<div>${e(err.message)}</div></div>
      <p style="margin-top:14px"><button class="knopf haupt" data-neu>Erneut versuchen</button></p></div></div>`;
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

function anmeldeRahmen(inhalt) {
  app.innerHTML = `<main class="anmeldung"><div class="karte">
    <div class="marke">${LOGO}<div><b>UntPlanWeb</b></div></div>
    ${sitzung?.schule?.name ? `<div class="schule">${e(sitzung.schule.name)}</div>` : '<div class="schule">Stundenplan und Vertretungen online</div>'}
    ${inhalt}</div></main>`;
  return app.querySelector('form');
}

function anmeldung() {
  document.title = 'Anmelden · UntPlanWeb';
  const form = anmeldeRahmen(`<form class="formular" autocomplete="on">
      <label class="feld">Benutzername<input name="login" autocomplete="username" required autofocus></label>
      <label class="feld">Passwort<input name="passwort" type="password" autocomplete="current-password" required></label>
      <div data-meldung></div>
      <button class="knopf haupt" type="submit">Anmelden</button>
    </form>`);
  form.onsubmit = async ev => {
    ev.preventDefault();
    const btn = form.querySelector('button');
    btn.disabled = true;
    try {
      await senden('/api/app/anmelden', { login: form.login.value.trim(), passwort: form.passwort.value });
      await start();
    } catch (err) {
      form.querySelector('[data-meldung]').innerHTML = `<div class="hinweis fehler">${icon('warnung')}<div>${e(err.message)}</div></div>`;
      form.passwort.select();
    } finally { btn.disabled = false; }
  };
}

function einrichtung() {
  document.title = 'Einrichtung · UntPlanWeb';
  const form = anmeldeRahmen(`<form class="formular">
      <div class="hinweis">${icon('info')}<div>Willkommen! Legen Sie die Schule und das erste Administrationskonto an.</div></div>
      <label class="feld">Name der Schule<input name="schulname" required autofocus placeholder="z. B. Gesamtschule Musterstadt"></label>
      <label class="feld">Benutzername<input name="login" value="admin" required autocomplete="username"></label>
      <label class="feld">Passwort (mindestens 8 Zeichen)<input name="passwort" type="password" minlength="8" required autocomplete="new-password"></label>
      <label class="feld">Passwort wiederholen<input name="passwort2" type="password" minlength="8" required autocomplete="new-password"></label>
      <div data-meldung></div>
      <button class="knopf haupt" type="submit">UntPlanWeb einrichten</button>
    </form>`);
  form.onsubmit = async ev => {
    ev.preventDefault();
    const meldung = form.querySelector('[data-meldung]');
    if (form.passwort.value !== form.passwort2.value) {
      meldung.innerHTML = `<div class="hinweis fehler">${icon('warnung')}<div>Die Passwörter stimmen nicht überein.</div></div>`;
      return;
    }
    try {
      const r = await senden('/api/app/einrichten', { schulname: form.schulname.value.trim(), login: form.login.value.trim(), passwort: form.passwort.value });
      sessionStorage.setItem('untplan:neuerSchluessel', JSON.stringify({ schulId: r.schulId, apiSchluessel: r.apiSchluessel }));
      location.hash = '#/admin/sync';
      await start();
    } catch (err) { meldung.innerHTML = `<div class="hinweis fehler">${icon('warnung')}<div>${e(err.message)}</div></div>`; }
  };
}

function passwortPflicht() {
  document.title = 'Passwort ändern · UntPlanWeb';
  const form = anmeldeRahmen(`<form class="formular">
      <div class="hinweis">${icon('schluessel')}<div>Bitte vergeben Sie vor der ersten Nutzung ein eigenes Passwort.</div></div>
      <label class="feld">Bisheriges Passwort<input name="alt" type="password" required autocomplete="current-password" autofocus></label>
      <label class="feld">Neues Passwort (mindestens 8 Zeichen)<input name="neu" type="password" minlength="8" required autocomplete="new-password"></label>
      <label class="feld">Neues Passwort wiederholen<input name="neu2" type="password" minlength="8" required autocomplete="new-password"></label>
      <div data-meldung></div>
      <button class="knopf haupt" type="submit">Passwort speichern</button>
      <button class="knopf flach" type="button" data-abmelden>Abmelden</button>
    </form>`);
  form.querySelector('[data-abmelden]').onclick = abmelden;
  form.onsubmit = async ev => {
    ev.preventDefault();
    const meldung = form.querySelector('[data-meldung]');
    if (form.neu.value !== form.neu2.value) {
      meldung.innerHTML = `<div class="hinweis fehler">${icon('warnung')}<div>Die Passwörter stimmen nicht überein.</div></div>`;
      return;
    }
    try { await senden('/api/app/passwort', { alt: form.alt.value, neu: form.neu.value }); await start(); }
    catch (err) { meldung.innerHTML = `<div class="hinweis fehler">${icon('warnung')}<div>${e(err.message)}</div></div>`; }
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
    { gruppe: 'Überblick' },
    { id: 'heute', text: 'Heute', icon: 'heute' },
    { id: 'plan', text: b.kollegium ? 'Stundenpläne' : 'Stundenplan', icon: 'plan' },
  ];
  if (b.kollegium) punkte.push({ id: 'tag', text: 'Tagesübersicht', icon: 'tag' });
  punkte.push({ id: 'vertretungen', text: 'Vertretungsplan', icon: 'vertretung' });
  punkte.push({ gruppe: 'Schulalltag' });
  punkte.push({ id: 'mitteilungen', text: 'Mitteilungen', icon: 'mitteilung', zaehler: true });
  punkte.push({ id: 'klassenbuch', text: b.kollegium ? 'Klassenbuch' : 'Hausaufgaben', icon: 'klassenbuch' });
  if (b.kollegium) punkte.push({ id: 'melden', text: 'Melden & Buchen', icon: 'melden' });
  if (b.admin || b.nachrichten) {
    punkte.push({ gruppe: 'Verwaltung' });
    punkte.push({ id: 'admin', text: b.admin ? 'Administration' : 'Nachrichten zum Tag', icon: 'admin' });
  }
  return punkte;
}

function huelle() {
  const b = sitzung.benutzer;
  app.innerHTML = `<div class="huelle">
    <nav class="navigation" aria-label="Hauptnavigation">
      <a class="marke" href="#/heute">${LOGO}<div><b>UntPlanWeb</b><small>${e(sitzung.schule.name || '')}</small></div></a>
      ${navPunkte().map(p => p.gruppe ? `<div class="nav-gruppe">${p.gruppe}</div>`
        : `<a class="nav-punkt" href="#/${p.id}" data-nav="${p.id}">${icon(p.icon)}<span>${p.text}</span>${p.zaehler ? '<span class="zaehler versteckt" data-zaehler></span>' : ''}</a>`).join('')}
      <div class="nav-fuss">
        <a class="ich" href="#/profil" data-nav="profil"><span class="avatar">${e(initialen(b.anzeigename || b.login))}</span>
          <div><b>${e(b.anzeigename || b.login)}</b><small>${e(b.rollenname)}${b.element ? ' · ' + e(b.element) : ''}</small></div></a>
        <button class="nav-punkt" data-abmelden style="border:0;background:none;width:100%;cursor:pointer;font:inherit">${icon('abmelden')}<span>Abmelden</span></button>
      </div>
    </nav>
    <div class="inhalt">
      <header class="kopf">
        <button class="knopf flach rund menue-knopf" data-menue aria-label="Menü">${icon('menue')}</button>
        <h1></h1><div class="rechts"></div>
      </header>
      <main class="seite" id="seite"></main>
    </div>
  </div>`;
  const h = app.querySelector('.huelle');
  app.querySelector('[data-menue]').onclick = () => h.classList.toggle('nav-offen');
  h.addEventListener('click', ev => {
    if (h.classList.contains('nav-offen') && (ev.target === h || ev.target.closest('.nav-punkt, .ich, .marke'))) h.classList.remove('nav-offen');
  });
  app.querySelector('[data-abmelden]').onclick = abmelden;
  zaehlerAktualisieren();
}

function zaehlerAktualisieren() {
  const z = document.querySelector('[data-zaehler]');
  if (!z || !sitzung) return;
  z.textContent = sitzung.ungelesen > 99 ? '99+' : sitzung.ungelesen;
  z.classList.toggle('versteckt', !sitzung.ungelesen);
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
  if (!ANSICHTEN[name] || !navPunkte().some(p => p.id === name) && name !== 'profil') name = 'heute';
  document.querySelectorAll('[data-nav]').forEach(a => a.classList.toggle('aktiv', a.dataset.nav === name));
  const seite = document.getElementById('seite');
  if (!seite) return;
  const nummer = laufendeAnsicht = Symbol(name);
  seite.innerHTML = '<div class="lade">Wird geladen …</div>';
  kontext.titel('');
  try {
    const modul = await ANSICHTEN[name]();
    if (laufendeAnsicht !== nummer) return;
    seite.innerHTML = '';
    seite.dataset.ansicht = name;
    await modul.zeigen(seite, kontext, teile.slice(1));
  } catch (err) {
    if (laufendeAnsicht !== nummer) return;
    if (err.status === 401) return;
    seite.innerHTML = `<div class="hinweis fehler">${icon('warnung')}<div>${e(err.message)}</div></div>`;
    if (err.status === undefined) console.error(err);
  }
}

/** Ist diese Ansicht noch die aktuelle? (gegen verspätete Antworten) */
kontext.aktuell = () => { const s = laufendeAnsicht; return () => s === laufendeAnsicht; };
kontext.fehler = fehler;

start();
