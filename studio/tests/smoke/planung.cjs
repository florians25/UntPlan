// Oberflächen-Smoke-Test der Module Planung, Optimierung und Zeitwünsche (Playwright, Chromium).
//
//   NODE_PATH=$(npm root -g) STUDIO_URL=http://127.0.0.1:5512 STUDIO_PASSWORT=geheim123 node studio/tests/smoke/planung.cjs
//
// Ablauf: Beispielschule öffnen → Klassenplan: Stunde wählen, Kontextmenü, Mehrfachauswahl, Ziehen mit Kollisionsanzeige,
// Raum zuordnen, Tauschvorschläge → Diagnose → Gewichtung → Zeitwünsche malen → Optimierung starten, Fortschritt, Ergebnis,
// Vergleich, Übernehmen. Bildschirmfotos nach STUDIO_SCREENSHOTS.
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const os = require('os');

const URL = process.env.STUDIO_URL || 'http://127.0.0.1:5512';
const BENUTZER = process.env.STUDIO_BENUTZER || 'admin';
const PASSWORT = process.env.STUDIO_PASSWORT || 'geheim123';
const BILDER = process.env.STUDIO_SCREENSHOTS || path.join(os.tmpdir(), 'studio-shots-planung');

async function api(pfad, optionen = {}) {
  const r = await fetch(URL + pfad, { ...optionen, headers: { 'X-UntPlan-Studio': '1', 'Content-Type': 'application/json', ...(optionen.headers || {}) } });
  if (!r.ok) throw new Error(`${pfad}: ${r.status} ${await r.text()}`);
  return r;
}

(async () => {
  fs.mkdirSync(BILDER, { recursive: true });
  const anm = await api('/api/anmeldung', { method: 'POST', body: JSON.stringify({ benutzer: BENUTZER, passwort: PASSWORT }) });
  const cookie = anm.headers.get('set-cookie').split(';')[0];
  await api('/api/dokumente/beispiel', { method: 'POST', body: '{}', headers: { cookie } }); // wird „zuletzt geöffnet“

  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1600, height: 950 } });
  const fehler = [];
  page.on('pageerror', (e) => fehler.push(String(e)));
  page.on('console', (m) => { if (m.type() === 'error' && !/status of 4\d\d/.test(m.text())) fehler.push(m.text()); });
  const foto = async (n) => { await page.waitForTimeout(250); await page.screenshot({ path: path.join(BILDER, n) }); };
  const reiter = (n) => page.click(`.ribbon .reiter:text-is("${n}")`);
  const knopf = (id) => page.click(`.rb-knopf[data-id="${id}"]`);
  const dialogZu = async () => { await page.keyboard.press('Escape'); await page.waitForTimeout(200); };

  try {
    await page.goto(URL);
    await page.fill('input[name=benutzer]', BENUTZER);
    await page.fill('input[name=passwort]', PASSWORT);
    await page.click('button[type=submit]');
    await page.waitForFunction(() => document.title.includes('Beispiel'), null, { timeout: 15000 });
    if (await page.isVisible('.backstage')) await page.keyboard.press('Escape');

    // Planung → Planen (Klassenplan)
    await reiter('Planung');
    await page.waitForSelector('.rb-knopf[data-id="planung.tausch"]:not(.geplant)');
    await foto('01-reiter-planung.png');
    await knopf('planung.planen');
    await page.waitForSelector('.fenster[data-typ=stundenplan] .r-eintrag');
    const eintrag = page.locator('.fenster[data-typ=stundenplan] .r-eintrag:not(.gekoppelt)').first();
    await eintrag.click();
    await eintrag.click({ button: 'right' });
    await page.waitForSelector('.menue');
    await foto('02-kontextmenue.png');
    await page.keyboard.press('Escape');

    // Mehrfachauswahl
    const zweiter = page.locator('.fenster[data-typ=stundenplan] .r-eintrag:not(.gekoppelt)').nth(3);
    await zweiter.click({ modifiers: ['Control'] });
    const markiert = await page.locator('.r-eintrag.markiert').count();
    if (markiert < 2) throw new Error(`Mehrfachauswahl: ${markiert} markiert`);
    await foto('03-mehrfachauswahl.png');
    await page.keyboard.press('Escape');

    // Ziehen mit Zielstatus und Kollisionsgrund (manuelles DnD über DataTransfer)
    await page.evaluate(async () => {
      const el = [...document.querySelectorAll('.fenster[data-typ=stundenplan] .r-eintrag[draggable=true]')][0];
      const dt = new DataTransfer();
      const r = el.getBoundingClientRect();
      el.dispatchEvent(new DragEvent('dragstart', { dataTransfer: dt, bubbles: true, clientX: r.left + 5, clientY: r.top + 5 }));
      await new Promise((ok) => setTimeout(ok, 700));
      const felder = [...document.querySelectorAll('.fenster[data-typ=stundenplan] .r-feld')];
      const ziel = felder.find((f) => f.classList.contains('ziel-Kollision')) || felder[felder.length - 1];
      const z = ziel.getBoundingClientRect();
      ziel.dispatchEvent(new DragEvent('dragover', { dataTransfer: dt, bubbles: true, cancelable: true, clientX: z.left + 10, clientY: z.top + 10 }));
      window.__dt = dt;
    });
    await page.waitForTimeout(200);
    const gefaerbt = await page.locator('.r-feld.ziel-Frei, .r-feld.ziel-TauschMoeglich, .r-feld.ziel-VorgabeVerletzt').count();
    if (!gefaerbt) throw new Error('Zielstatus wird beim Ziehen nicht angezeigt.');
    await foto('04-ziehen-kollisionsanzeige.png');
    await page.evaluate(() => document.querySelector('.fenster[data-typ=stundenplan] .r-eintrag[draggable=true]').dispatchEvent(new DragEvent('dragend', { dataTransfer: window.__dt, bubbles: true })));

    // Raum zuordnen
    await eintrag.click();
    await knopf('planung.raum');
    await page.waitForSelector('.pl-raum .pl-raeume tbody tr');
    await foto('05-raum-zuordnen.png');
    await page.locator('.pl-raeume tbody tr:not(.besetzt):not(.gesperrt)').first().dblclick();
    await page.waitForTimeout(500);
    await dialogZu();

    // Tauschvorschläge
    await eintrag.click();
    await knopf('planung.tausch');
    await page.waitForSelector('.pl-tausch tbody tr');
    await page.waitForFunction(() => !document.querySelector('.pl-tausch .ladeanzeige'), null, { timeout: 30000 });
    await foto('06-tauschvorschlaege.png');
    await dialogZu();

    // Diagnose
    await knopf('planung.diagnose');
    await page.waitForSelector('.op-diagnose .op-zeile.punkt');
    await page.click('.op-reiter button:text("Stundenplan")');
    await page.waitForTimeout(300);
    await foto('07-diagnose.png');

    // Gewichtung
    await knopf('planung.gewichtung');
    await page.waitForSelector('.op-gewichtung input[type=range]');
    await foto('08-gewichtung.png');
    await page.click('.op-karten button:text("Gewichtungs-Verteilung")');
    await foto('08b-gewichtung-verteilung.png');
    await dialogZu();

    // Zeitwünsche malen
    await reiter('Dateneingabe');
    await knopf('eingabe.zeitwuensche');
    await page.waitForSelector('.zw-tabelle td[data-tag]');
    await page.click('.zw-wert[data-wert="-3"]');
    const a = await page.locator('.zw-tabelle td[data-tag="0"][data-spalte="5"]').boundingBox();
    const b = await page.locator('.zw-tabelle td[data-tag="0"][data-spalte="7"]').boundingBox();
    await page.mouse.move(a.x + 5, a.y + 5);
    await page.mouse.down();
    await page.mouse.move(b.x + 5, b.y + 5, { steps: 6 });
    await page.mouse.up();
    await page.waitForTimeout(600);
    await page.click('.zw-wert[data-wert="2"]');
    await page.click('.zw-tabelle td[data-tag="2"][data-spalte="0"]');
    await page.waitForTimeout(500);
    const gesetzt = await page.locator('.zw-tabelle td.zw-w-3').count();
    if (gesetzt < 3) throw new Error(`Zeitwünsche: ${gesetzt} Felder −3`);
    await foto('09-zeitwuensche.png');

    // Optimierung
    await reiter('Planung');
    await knopf('planung.optimierung');
    await page.waitForSelector('.op-steuerdaten');
    await page.selectOption('.op-steuerdaten select[name=strategie]', 'A');
    await page.fill('.op-steuerdaten input[name=anzahlPlaene]', '3');
    await foto('10-steuerdaten.png');
    await page.click('.dialog .knopf.primaer');
    await page.waitForSelector('.fenster[data-typ="optimierung.lauf"]');
    await page.waitForTimeout(400);
    await foto('11-optimierung-laeuft.png');
    await page.waitForSelector('.op-kennzahlen tbody tr.gewaehlt', { timeout: 120000 });
    await foto('12-optimierung-ergebnis.png');
    await page.click('.fenster[data-typ="optimierung.lauf"] .wz-knopf:has-text("Vergleich")');
    await page.waitForSelector('.op-plaene .r-eintrag', { timeout: 15000 });
    await page.locator('.fenster[data-typ="optimierung.lauf"]').evaluate((el) => el.querySelector('.fenster-titel').dispatchEvent(new MouseEvent('dblclick', { bubbles: true })));
    await page.waitForTimeout(500);
    await foto('13-ergebnisvergleich.png');
    await page.click('.fenster[data-typ="optimierung.lauf"] .wz-knopf:has-text("Plan übernehmen")');
    await page.waitForSelector('.op-gross.uebernommen', { timeout: 15000 });
    await foto('14-uebernommen.png');

    // Schmales Fenster (Telefon)
    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(400);
    await foto('15-schmal.png');

    if (fehler.length) throw new Error('Fehler in der Konsole:\n' + fehler.join('\n'));
    console.log(`Smoke Planung OK – Bildschirmfotos in ${BILDER}`);
  } catch (e) {
    await foto('fehler.png').catch(() => {});
    console.error(e);
    process.exitCode = 1;
  } finally {
    await browser.close();
  }
})();
