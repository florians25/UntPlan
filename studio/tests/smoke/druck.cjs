// Oberflächen-Smoke-Test der Module Einstellungen, Druck, Übersichtspläne und Stundenplanformate (Playwright, Chromium).
//
//   NODE_PATH=$(npm root -g) STUDIO_URL=http://127.0.0.1:5513 STUDIO_PASSWORT=… node studio/tests/smoke/druck.cjs
//
// Ablauf: Anmelden → Beispielschule (mit Stundenplan) öffnen → Einstellungen, Zeitraster, Kalender/Ferien, Perioden,
// Unterrichtsgruppen → Lehrer-Übersicht → Druckauswahl mit Vorschau → Berichte → Stundenplanformate → Vertretungsplan-Ausgabe.
// Bildschirmfotos nach STUDIO_SCREENSHOTS.
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const os = require('os');

const URL = process.env.STUDIO_URL || 'http://127.0.0.1:5501';
const BENUTZER = process.env.STUDIO_BENUTZER || 'admin';
const PASSWORT = process.env.STUDIO_PASSWORT || 'geheim123';
const BILDER = process.env.STUDIO_SCREENSHOTS || path.join(os.tmpdir(), 'studio-shots-druck');

(async () => {
  fs.mkdirSync(BILDER, { recursive: true });
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1600, height: 950 } });
  const fehler = [];
  page.on('pageerror', (e) => fehler.push(String(e)));
  page.on('console', (m) => { if (m.type() === 'error' && !/Failed to load resource/.test(m.text())) fehler.push(m.text()); });
  const foto = (name) => page.screenshot({ path: path.join(BILDER, `${name}.png`) });
  const reiter = (name) => page.click(`.ribbon .reiter:text-is("${name}")`);
  const knopf = (id) => page.click(`.rb-knopf[data-id="${id}"]`);
  const alleZu = async () => { await reiter('Start'); await knopf('start.fenster.alle'); };

  try {
    await page.goto(URL);
    await page.fill('input[name=benutzer]', BENUTZER);
    await page.fill('input[name=passwort]', PASSWORT);
    await page.click('button[type=submit]');
    await page.waitForSelector('.ribbon .reiter.datei');
    // Beispielschule mit Stundenplan über die API anlegen und öffnen
    await page.evaluate(async () => {
      const r = await fetch('/api/dokumente/beispiel', { method: 'POST', headers: { 'X-UntPlan-Studio': '1', 'Content-Type': 'application/json' }, body: '{}' });
      const stand = await r.json();
      const { dokumentOeffnen } = await import('/js/kern/dokument.js');
      await dokumentOeffnen(stand.id);
    });
    await page.waitForFunction(() => document.title.includes('Beispiel'), null, { timeout: 20000 });
    await page.waitForTimeout(500);
    await page.keyboard.press('Escape');

    // Einstellungen (Schuldaten) – Seite Vertretungsplanung
    await reiter('Start');
    await knopf('start.einstellungen');
    await page.waitForSelector('.fenster[data-typ="einstellungen.schule"] .es-seite input');
    await page.click('.fenster[data-typ="einstellungen.schule"] .es-knoten:text-is("Vertretungsplanung")');
    await page.waitForSelector('.es-gewichtung');
    await foto('01-einstellungen');
    await page.click('.fenster[data-typ="einstellungen.schule"] .es-knoten:text-is("Allgemein")');
    const name = page.locator('.fenster[data-typ="einstellungen.schule"] .es-seite input').first();
    await name.fill('Städtisches Beispiel-Gymnasium Smoke');
    await page.click('.fenster[data-typ="einstellungen.schule"] .wz-knopf:has-text("Übernehmen")');
    await page.waitForFunction(() => document.title.includes('Smoke') || document.querySelector('.statuszeile')?.textContent.includes('übernommen'), null, { timeout: 5000 }).catch(() => {});
    await alleZu();

    // Zeitraster
    await reiter('Dateneingabe');
    await knopf('eingabe.zeitraster');
    await page.waitForSelector('.es-zr .es-zr-zelle');
    await page.locator('.es-zr .es-zr-zelle').last().click();
    await page.click('.es-eintragung button:has-text("leer")');
    await foto('02-zeitraster');
    await page.click('.fenster[data-typ="einstellungen.zeitraster"] .es-reiterknopf:text-is("Pausen")');
    await page.waitForSelector('.fenster[data-typ="einstellungen.zeitraster"] .es-liste');
    await foto('03-zeitraster-pausen');
    await alleZu();

    // Kalender / Ferien
    await reiter('Dateneingabe');
    await knopf('eingabe.ferien');
    await page.waitForSelector('.es-monat td.tag');
    await page.click('.es-monat td.schultag >> nth=20');
    await page.waitForSelector('.es-tag b');
    await foto('04-kalender-ferien');
    await alleZu();

    // Perioden und Unterrichtsgruppen
    await reiter('Start');
    await knopf('start.perioden');
    await page.waitForSelector('.es-perioden-liste tr.gewaehlt');
    await foto('05-perioden');
    await alleZu();
    await reiter('Dateneingabe');
    await knopf('eingabe.ugruppen');
    await page.waitForSelector('.fenster[data-typ="einstellungen.ugruppen"] .es-liste');
    await page.locator('.fenster[data-typ="einstellungen.ugruppen"] tr.neu input').first().fill('A-Wo');
    await page.locator('.fenster[data-typ="einstellungen.ugruppen"] tr.neu input').first().press('Tab');
    await page.waitForFunction(() => [...document.querySelectorAll('.fenster[data-typ="einstellungen.ugruppen"] tr:not(.neu) input')].some((i) => i.value === 'A-Wo'));
    await foto('06-unterrichtsgruppen');
    await alleZu();

    // Übersichtspläne
    await reiter('Stundenpläne');
    await knopf('stundenplaene.uebersicht.lehrer');
    await page.waitForSelector('.fenster[data-typ=uebersicht] .ue-zelle');
    await page.locator('.fenster[data-typ=uebersicht] .fenster-titel').dblclick();
    await page.waitForTimeout(300);
    await foto('07-lehrer-uebersicht');
    await alleZu();
    await reiter('Stundenpläne');
    await knopf('stundenplaene.uebersicht.raum');
    await page.waitForSelector('.fenster[data-typ=uebersicht] .ue-zelle');
    await foto('08-raum-uebersicht');
    await alleZu();

    // Druckauswahl → Seitenansicht (PDF)
    await reiter('Stundenpläne');
    await knopf('stundenplaene.drucken');
    await page.waitForSelector('.dr-element');
    await foto('09-druckauswahl');
    const [antwort] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/druck/stundenplaene') && r.status() === 200),
      page.click('.fenster[data-typ="druck.auswahl"] button:has-text("Seitenansicht")'),
    ]);
    if (!(antwort.headers()['content-type'] || '').includes('application/pdf')) throw new Error('Keine PDF-Antwort');
    await page.waitForSelector('.fenster[data-typ="druck.vorschau"] iframe');
    await page.waitForTimeout(1200);
    await foto('10-druckvorschau');
    // HTML-Ausgabe (wird auch im kopflosen Browser angezeigt)
    await page.locator('.fenster[data-typ="druck.vorschau"] .fenster-titel button.zu').click();
    await page.click('.fenster[data-typ="druck.auswahl"] button:has-text("HTML-Ausgabe")');
    await page.waitForSelector('.fenster[data-typ="druck.vorschau"] iframe');
    await page.waitForTimeout(1200);
    await foto('10b-html-ausgabe');
    await alleZu();

    // Berichte
    await reiter('Stundenpläne');
    await knopf('stundenplaene.berichte');
    await page.waitForSelector('.dr-bericht.aktiv');
    await page.waitForTimeout(800);
    await foto('11-berichte');
    await alleZu();

    // Stundenplanformate: Format aus Vorlage anlegen, Feld-Editor
    await reiter('Stundenpläne');
    await knopf('stundenplaene.formate');
    await page.waitForSelector('.fm-liste');
    await page.click('.fenster[data-typ="formate.editor"] .wz-knopf:has-text("Neu")');
    await page.fill('.dialog input[name=neu]', 'Klasse Smoke');
    await page.click('.dialog button:has-text("Anlegen")');
    await page.waitForSelector('.fm-eintrag.aktiv:has-text("Klasse Smoke")');
    await page.click('.es-reiterknopf:text-is("Stundenplan-Stunde")');
    await page.click('button:has-text("Feld hinzufügen")');
    await page.click('button:has-text("Feld hinzufügen")');
    await page.click('.fenster[data-typ="formate.editor"] .wz-knopf:has-text("Speichern")');
    await page.waitForTimeout(500);
    await foto('12-stundenplanformate');
    await alleZu();

    // Vertretungsplan-Ausgabe im Vertretungsmodus
    await reiter('Start');
    await knopf('start.vertretungsmodus');
    await reiter('Vertretungsplanung');
    await knopf('vertretung.ausgabe');
    await page.waitForSelector('.dr-felder');
    await page.waitForTimeout(1000);
    await foto('13-vertretungsplan-ausgabe');
    await reiter('Vertretungsplanung');
    await knopf('vertretung.modus');
  } catch (e) {
    await foto('fehler').catch(() => {});
    console.error('Seitenfehler:', fehler.join('\n'));
    throw e;
  }
  await browser.close();
  if (fehler.length) {
    console.error('Fehler in der Seite:\n' + fehler.join('\n'));
    process.exit(1);
  }
  console.log(`Smoke-Test Druck/Einstellungen bestanden. Bildschirmfotos: ${BILDER}`);
})().catch((e) => { console.error(e); process.exit(1); });
