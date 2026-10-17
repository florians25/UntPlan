// Oberflächen-Smoke-Test des Vertretungsmodus (Playwright, Chromium).
//
//   NODE_PATH=$(npm root -g) STUDIO_URL=http://127.0.0.1:5511 STUDIO_PASSWORT=geheim123 node studio/tests/smoke/vertretung.cjs
//
// Ablauf: Anmelden → Beispielschule → Vertretungsmodus → Absenz in der Liste eingeben (Zeilen-Commit) → Vertretungen (offene ???) →
// Vertretungsvorschlag übernehmen → Entfall mit „-“ → Kalender, tagesaktueller Lehrerplan, Veranstaltung mit allen Lehrkräften →
// Standardanordnung → Tag blättern (Strg+Bild↓). Bildschirmfotos nach STUDIO_SCREENSHOTS.
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const os = require('os');

const URL = process.env.STUDIO_URL || 'http://127.0.0.1:5501';
const BENUTZER = process.env.STUDIO_BENUTZER || 'admin';
const PASSWORT = process.env.STUDIO_PASSWORT || 'geheim123';
const BILDER = process.env.STUDIO_SCREENSHOTS || path.join(os.tmpdir(), 'studio-shots-vertretung');

(async () => {
  fs.mkdirSync(BILDER, { recursive: true });
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1680, height: 980 } });
  const fehler = [];
  page.on('pageerror', (e) => fehler.push(String(e)));
  if (process.env.DEBUG) page.on('request', (r) => { if (r.method() !== 'GET') console.log('>>', r.method(), r.url().replace(/.*dokumente\/[0-9a-f]+/, ''), r.postData()?.slice(0, 200)); });
  page.on('console', (m) => { if (m.type() === 'error' && !/status of 4\d\d/.test(m.text())) fehler.push(m.text()); });
  const foto = (name) => page.screenshot({ path: path.join(BILDER, name) });
  const schritt = (t) => console.log(`· ${t}`);
  try {
    await page.goto(URL);
    await page.fill('input[name=benutzer]', BENUTZER);
    await page.fill('input[name=passwort]', PASSWORT);
    await page.click('button[type=submit]');
    await page.waitForSelector('.ribbon .reiter.datei');
    const kopf = { 'X-UntPlan-Studio': '1' };
    const stand = await (await page.request.post(`${URL}/api/dokumente/beispiel`, { headers: kopf, data: {} })).json();
    const dok = `${URL}/api/dokumente/${stand.id}`;
    await page.evaluate((id) => sessionStorage.setItem('studio.dokument', id), stand.id);
    await page.reload();
    await page.waitForFunction(() => document.title.includes('Beispiel'), null, { timeout: 20000 });
    schritt('Beispielschule geöffnet');

    // Lehrkraft mit Unterricht am Planungsdatum
    const datum = (await (await page.request.get(`${dok}/vertretung/start`)).json()).datum;
    const lehrer = (await (await page.request.get(`${dok}/elemente?typen=lehrer`)).json()).lehrer;
    let kurz = null;
    for (const l of lehrer) {
      const plan = await (await page.request.get(`${dok}/vertretung/plan/lehrer/${l.id}?datum=${datum}`)).json();
      if (plan.zellen.filter((z) => z.datum === datum).length >= 3) { kurz = l.k; break; }
    }
    if (!kurz) throw new Error('keine Lehrkraft mit Unterricht');

    // Vertretungsmodus
    await page.click('.rb-knopf[data-id="start.vertretungsmodus"]');
    await page.waitForSelector('.ribbon .reiter.aktiv:text-is("Vertretungsplanung")');
    await page.waitForFunction((d) => document.querySelector('.vt-rb-datum')?.value === d, datum);
    await foto('01-vertretungsmodus-menueband.png');
    schritt(`Vertretungsmodus, Datum ${datum}, Lehrkraft ${kurz}`);

    // Absenz direkt in der Liste eingeben (neue Zeile, Element, Zeile verlassen = speichern)
    await page.click('.rb-knopf[data-id="vertretung.absenzen"]');
    await page.click('.menue .m-eintrag:has-text("Absenzen")');
    await page.waitForSelector('.fenster[data-typ="vertretung.absenzen"] .t-zeile.t-neu');
    await page.click('.fenster[data-typ="vertretung.absenzen"] .t-zeile.t-neu .t-zelle >> nth=1');
    await page.keyboard.type(kurz);
    await page.keyboard.press('Tab');
    await page.waitForSelector('.fenster[data-typ="vertretung.absenzen"] .t-zeile.vt-entwurf');
    await page.keyboard.press('Control+Enter');
    await page.waitForFunction((k) => [...document.querySelectorAll('.fenster[data-typ="vertretung.absenzen"] .t-zeile:not(.vt-entwurf):not(.t-neu)')].some((z) => z.textContent.includes(k)), kurz, { timeout: 15000 });
    await page.waitForTimeout(600);
    if (await page.locator('.hinweise :text("bereits vorhanden")').count()) throw new Error('Absenz wurde doppelt gespeichert');
    await foto('02-absenzen.png');
    schritt('Absenz gespeichert');

    // Vertretungen mit offenen Stunden
    await page.click('.rb-knopf[data-id="vertretung.vertretungen"]');
    await page.click('.menue .m-eintrag:has-text("Vertretungen")');
    const liste = '.fenster[data-typ="vertretung.liste"]';
    await page.waitForFunction((s) => [...document.querySelectorAll(`${s} .t-zelle.vt-rot`)].some((z) => z.textContent === '???'), liste, { timeout: 15000 });
    await foto('03-vertretungen-offen.png');
    schritt('Vertretungen mit ??? angezeigt');

    // erste offene Zeile wählen, Vorschlag öffnen und den ersten Kandidaten übernehmen
    const offeneZeile = () => page.evaluate((s) => {
      const z = [...document.querySelectorAll(`${s} .t-zeile`)].find((x) => [...x.querySelectorAll('.t-zelle.vt-rot')].some((c) => c.textContent === '???'));
      return z ? { r: z.dataset.r, nr: z.children[0].textContent.trim() } : null;
    }, liste);
    const o1 = await offeneZeile();
    const nr = o1.nr;
    await page.click(`${liste} .t-zeile[data-r="${o1.r}"] .t-zelle >> nth=0`);
    await page.keyboard.press('Enter');
    const vorschlag = '.fenster[data-typ="vertretung.vorschlag"]';
    await page.waitForSelector(`${vorschlag} .vt-tabelle tbody tr`, { timeout: 15000 });
    await page.waitForFunction((s) => document.querySelector(`${s} .vt-klapp .text`)?.textContent.length > 5, vorschlag);
    await foto('04-vertretungsvorschlag.png');
    const kandidat = (await page.locator(`${vorschlag} .vt-tabelle:not([hidden]) tbody tr`).first().locator('td').first().textContent()).trim();
    await page.locator(`${vorschlag} .vt-tabelle:not([hidden]) tbody tr`).first().dblclick();
    await page.waitForFunction(([s, n, k]) => [...document.querySelectorAll(`${s} .t-zeile`)].some((z) => z.children[0]?.textContent.trim() === n && z.textContent.includes(k)), [liste, nr, kandidat], { timeout: 15000 });
    schritt(`Vorschlag übernommen: Vtr-Nr. ${nr} → ${kandidat}`);
    await foto('05-vorschlag-uebernommen.png');

    // Entfall mit „-“ auf der nächsten offenen Zeile
    await page.click('button[title^="Vertretungen /"]');
    const o2 = await offeneZeile();
    if (o2) {
      const nr2 = o2.nr;
      await page.click(`${liste} .t-zeile[data-r="${o2.r}"] .t-zelle >> nth=0`);
      await page.keyboard.press('-');
      await page.waitForFunction(([s, n]) => [...document.querySelectorAll(`${s} .t-zeile`)].some((z) => z.children[0]?.textContent.trim() === n && z.textContent.includes('---')), [liste, nr2], { timeout: 15000 });
      schritt(`Entfall Vtr-Nr. ${nr2}`);
    }
    // Kontextmenü
    await page.locator(`${liste} .t-zeile`).first().locator('.t-zelle').nth(2).click({ button: 'right' });
    await page.waitForSelector('.menue .m-eintrag');
    await foto('06-kontextmenue.png');
    await page.keyboard.press('Escape');

    // Kalender, tagesaktueller Lehrerplan, Bereitschaften, Vormerkungen
    await page.click('.rb-knopf[data-id="vertretung.kalender"]');
    await page.waitForSelector('.menue .m-eintrag');
    await page.click('.menue .m-eintrag:has-text("Kalender")');
    await page.waitForSelector('.fenster[data-typ="vertretung.kalender"] .vt-tabelle tbody tr');
    await page.click('.rb-knopf[data-id="vertretung.planungsdialog"]');
    await page.waitForSelector('.fenster[data-typ="vertretung.plan"] .r-eintrag', { timeout: 15000 });
    await foto('07-planungsdialog-tagesaktuell.png');
    schritt('Planungsdialog (Klassen- und Lehrerplan tagesaktuell)');

    // Veranstaltung mit allen Lehrkräften und Klassen
    await page.click('.rb-knopf[data-id="vertretung.veranstaltungen"]');
    const va = '.fenster[data-typ="veranstaltungen.liste"]';
    await page.waitForSelector(`${va} .t-zeile.t-neu`);
    await page.click(`${va} .t-zeile.t-neu .t-zelle >> nth=3`);
    await page.keyboard.type('Alle');
    await page.keyboard.press('Escape').catch(() => {});
    await page.keyboard.press('Enter');
    await page.waitForFunction((s) => [...document.querySelectorAll(`${s} .t-zelle`)].some((z) => z.textContent === 'Alle Lehrkräfte'), va, { timeout: 15000 });
    schritt('Veranstaltung „Alle Lehrkräfte“ angelegt');
    await foto('08-veranstaltung-alle.png');

    // Standardanordnung
    await page.click('.rb-knopf[data-id="vertretung.formate"]');
    await page.click('.menue .m-eintrag:has-text("Standardanordnung")');
    await page.waitForSelector(`${liste} .t-zeile`, { timeout: 15000 });
    await page.waitForSelector('.fenster[data-typ="vertretung.plan"] .r-eintrag', { timeout: 15000 });
    await page.waitForTimeout(800);
    await foto('09-standardanordnung.png');

    // Tag blättern mit Strg+Bild↓
    await page.locator(`${liste} .t-zeile`).first().click().catch(() => {});
    await page.keyboard.press('Control+PageDown');
    await page.waitForFunction((d) => document.querySelector('.vt-rb-datum')?.value > d, datum, { timeout: 10000 });
    await page.waitForTimeout(800);
    await foto('10-naechster-tag.png');
    schritt(`Tag gewechselt: ${await page.inputValue('.vt-rb-datum')}`);

    // Einstellungen-Dialog
    await page.click('.rb-knopf[data-id="vertretung.einstellungen"]');
    await page.click('.menue .m-eintrag:has-text("Vertretungsvorschlag")');
    await page.waitForSelector('.vt-einstellungen');
    await foto('11-einstellungen.png');
    await page.keyboard.press('Escape');
  } catch (e) {
    await page.screenshot({ path: path.join(BILDER, 'fehler.png') }).catch(() => {});
    console.error('Smoke-Test fehlgeschlagen:', e);
    if (fehler.length) console.error('Seitenfehler:', fehler.join('\n'));
    await browser.close();
    process.exit(1);
  }
  await browser.close();
  if (fehler.length) {
    console.error(`Fehler in der Seite:\n${fehler.join('\n')}`);
    process.exit(1);
  }
  console.log(`Smoke-Test Vertretungsmodus erfolgreich, Bilder in ${BILDER}`);
})();
