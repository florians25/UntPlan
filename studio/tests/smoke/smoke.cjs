// Oberflächen-Smoke-Test für UntPlan Studio (Playwright, Chromium).
//
//   NODE_PATH=$(npm root -g) node studio/tests/smoke/smoke.cjs
//
// Umgebungsvariablen: STUDIO_URL (Standard http://127.0.0.1:5501), STUDIO_BENUTZER (admin), STUDIO_PASSWORT,
// STUDIO_SCREENSHOTS (Ordner für Bildschirmfotos), PLAYWRIGHT_BROWSERS_PATH (z. B. /opt/pw-browsers).
// Ablauf: Anmelden → Beispiel-Schuldatei als .up hochladen (Datei → Öffnen) → Fenster Lehrkräfte und Klassenplan öffnen →
// Wert in der Tabelle ändern → Fenster nebeneinander anordnen → Bildschirmfotos.
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const os = require('os');

const URL = process.env.STUDIO_URL || 'http://127.0.0.1:5501';
const BENUTZER = process.env.STUDIO_BENUTZER || 'admin';
const PASSWORT = process.env.STUDIO_PASSWORT || 'geheim123';
const BILDER = process.env.STUDIO_SCREENSHOTS || path.join(os.tmpdir(), 'studio-shots');

async function api(pfad, optionen = {}) {
  const r = await fetch(URL + pfad, { ...optionen, headers: { 'X-UntPlan-Studio': '1', 'Content-Type': 'application/json', ...(optionen.headers || {}) } });
  if (!r.ok) throw new Error(`${pfad}: ${r.status} ${await r.text()}`);
  return r;
}

(async () => {
  fs.mkdirSync(BILDER, { recursive: true });
  // Beispiel-Schuldatei (.up) über die API erzeugen und herunterladen
  const anm = await api('/api/anmeldung', { method: 'POST', body: JSON.stringify({ benutzer: BENUTZER, passwort: PASSWORT }) });
  const cookie = anm.headers.get('set-cookie').split(';')[0];
  const stand = await (await api('/api/dokumente/beispiel', { method: 'POST', body: '{}', headers: { cookie } })).json();
  const up = Buffer.from(await (await api(`/api/dokumente/${stand.id}/herunterladen`, { headers: { cookie } })).arrayBuffer());
  await api(`/api/dokumente/${stand.id}`, { method: 'DELETE', headers: { cookie } });
  const upDatei = path.join(BILDER, 'Beispielschule.up');
  fs.writeFileSync(upDatei, up);

  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1600, height: 950 } });
  const fehler = [];
  page.on('pageerror', (e) => fehler.push(String(e)));
  page.on('console', (m) => { if (m.type() === 'error') fehler.push(m.text()); });

  try {
  await page.goto(URL);
  await page.fill('input[name=benutzer]', BENUTZER);
  await page.fill('input[name=passwort]', PASSWORT);
  await page.click('button[type=submit]');
  await page.waitForSelector('.ribbon .reiter.datei');
  await page.screenshot({ path: path.join(BILDER, '01-start.png') });

  // Datei → Öffnen → Hochladen
  // ohne zuletzt geöffnete Datei (frische Installation) ist die Dateiansicht schon offen
  if (!(await page.isVisible('.backstage'))) await page.click('.ribbon .reiter.datei');
  await page.click('.backstage nav button[data-seite=oeffnen]');
  const [chooser] = await Promise.all([page.waitForEvent('filechooser'), page.click('.backstage .ablage button')]);
  await chooser.setFiles(upDatei);
  await page.waitForFunction(() => document.title.startsWith('Beispielschule'), null, { timeout: 15000 });
  await page.waitForSelector('.backstage', { state: 'detached' });

  // Dateneingabe → Lehrer
  await page.click('.ribbon .reiter:text-is("Dateneingabe")');
  await page.click('.rb-knopf[data-id="eingabe.lehrer"]');
  await page.waitForSelector('.fenster[data-typ=stammdaten] .t-zeile');
  // Wert ändern: erste Zeile, Spalte Nachname
  const zelle = page.locator('.fenster[data-typ=stammdaten] .t-zeile[data-r="0"] .t-zelle').nth(1);
  await zelle.dblclick();
  await page.keyboard.press('Control+A');
  await page.keyboard.type('Smoketest');
  await page.keyboard.press('Enter');
  await page.waitForFunction(() => [...document.querySelectorAll('.fenster[data-typ=stammdaten] .t-zelle')].some((z) => z.textContent === 'Smoketest'));

  // Stundenpläne → Klassenplan
  await page.click('.ribbon .reiter:text-is("Stundenpläne")');
  await page.click('.rb-knopf[data-id="stundenplaene.klasse"]');
  await page.waitForSelector('.fenster[data-typ=stundenplan] .r-eintrag');
  await page.click('.ribbon .reiter:text-is("Start")');
  await page.click('.rb-knopf[data-id="start.fenster.nebeneinander"]');
  await page.waitForTimeout(400);
  await page.screenshot({ path: path.join(BILDER, '02-lehrkraefte-stundenplan.png') });

  // Unterricht der Klasse
  await page.click('.ribbon .reiter:text-is("Dateneingabe")');
  await page.click('.rb-knopf[data-id="eingabe.unterricht.klassen"]');
  await page.waitForSelector('.fenster[data-typ=unterricht] .t-zeile');
  await page.click('.ribbon .reiter:text-is("Start")');
  await page.click('.rb-knopf[data-id="start.fenster.untereinander"]');
  await page.waitForTimeout(400);
  await page.screenshot({ path: path.join(BILDER, '03-drei-fenster.png') });

  // Vertretungsmodus (grün)
  await page.click('.rb-knopf[data-id="start.vertretungsmodus"]');
  await page.waitForTimeout(300);
  await page.screenshot({ path: path.join(BILDER, '04-vertretungsmodus.png') });
  await page.click('.rb-knopf[data-id="vertretung.modus"]');
  } catch (e) {
    await page.screenshot({ path: path.join(BILDER, 'fehler.png') }).catch(() => {});
    console.error('Seitenfehler:', fehler.join('\n'));
    throw e;
  }

  await browser.close();
  if (fehler.length) {
    console.error('Fehler in der Seite:\n' + fehler.join('\n'));
    process.exit(1);
  }
  console.log(`Smoke-Test bestanden. Bildschirmfotos: ${BILDER}`);
})().catch((e) => { console.error(e); process.exit(1); });
