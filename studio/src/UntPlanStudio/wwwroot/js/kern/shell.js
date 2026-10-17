// App-Shell: Titelleiste mit Schnellzugriff, Menüband, Fenster-Arbeitsbereich, Statuszeile, Anmeldung.
import { h, leeren, datumDe } from './dom.js';
import { symbol } from './symbole.js';
import { an, melden } from './ereignisse.js';
import { zustand } from './zustand.js';
import { api, einstellungLesen, einstellungSchreiben } from './api.js';
import { Ribbon } from './ribbon.js';
import { fensterverwaltung } from './fenster.js';
import { speichern, rueckgaengig, wiederholen, herunterladen } from './dokument.js';
import { fehlerMelden } from './dialog.js';
import { tasteBehandeln, befehl } from './modulsystem.js';

export const shell = { ribbon: null };

/** Anmeldedialog; liefert den Benutzer nach erfolgreicher Anmeldung. */
export function anmelden() {
  return new Promise((fertig) => {
    const fehler = h('div', { class: 'fehlertext', role: 'alert' });
    const name = h('input', { class: 'eingabe', name: 'benutzer', autocomplete: 'username', required: true });
    const pw = h('input', { class: 'eingabe', name: 'passwort', type: 'password', autocomplete: 'current-password', required: true });
    const knopf = h('button', { class: 'knopf primaer', type: 'submit' }, 'Anmelden');
    const form = h('form', null,
      h('div', { class: 'kopf' }, symbol('logo'), h('div', null, h('h1', null, zustand.system?.anwendung || 'UntPlan Studio (Beta)'), h('div', { class: 'leise' }, 'Stundenplanung im Browser'))),
      h('label', null, 'Benutzername', name), h('label', null, 'Passwort', pw), fehler, knopf,
      h('div', { class: 'hinweis' }, 'Beta-Version: Bitte wichtige Schuldateien zusätzlich mit der Desktop-Anwendung sichern.'));
    const ueberlagerung = h('div', { class: 'anmeldung' }, form);
    form.addEventListener('submit', async (e) => {
      e.preventDefault();
      knopf.disabled = true;
      fehler.textContent = '';
      try {
        const b = await api.post('/api/anmeldung', { benutzer: name.value, passwort: pw.value });
        ueberlagerung.remove();
        fertig(b);
      } catch (f) {
        fehler.textContent = f.message;
        pw.select();
      } finally { knopf.disabled = false; }
    });
    document.body.append(ueberlagerung);
    name.focus();
  });
}

/** Baut die Oberfläche auf. */
export function shellAufbauen(wurzel) {
  leeren(wurzel);
  // ---- Titelleiste ----
  const qa = (sym, titel, f, id) => h('button', { type: 'button', title: titel, dataset: { qa: id }, onclick: () => Promise.resolve().then(f).catch(fehlerMelden) }, symbol(sym));
  const qaSpeichern = qa('speichern', 'Speichern (Strg+S)', speichern, 'speichern');
  const qaZurueck = qa('rueckgaengig', 'Rückgängig (Strg+Z)', rueckgaengig, 'rueckgaengig');
  const qaVor = qa('wiederholen', 'Wiederholen (Strg+Y)', wiederholen, 'wiederholen');
  const qaLaden = qa('herunterladen', 'Schuldatei herunterladen (.up)', herunterladen, 'herunterladen');
  const titel = h('div', { class: 'titel' });
  const konto = h('div', { class: 'konto' });
  wurzel.append(h('header', { class: 'titelleiste' }, h('span', { class: 'logo' }, symbol('logo')), h('div', { class: 'schnellzugriff' }, qaSpeichern, qaZurueck, qaVor, qaLaden), titel, konto));

  // ---- Menüband ----
  const ribbonPlatz = h('div');
  wurzel.append(ribbonPlatz);
  shell.ribbon = new Ribbon(ribbonPlatz);

  // ---- Arbeitsbereich ----
  const flaeche = h('div', { class: 'flaeche' });
  const leiste = h('div', { class: 'fensterleiste' });
  const willkommen = h('div', { class: 'willkommen' });
  flaeche.append(willkommen);
  wurzel.append(h('main', { class: 'arbeitsbereich' }, flaeche, leiste));
  let wiederherstellen = false; // während des Wiederherstellens keine Anordnung speichern
  fensterverwaltung.einrichten(flaeche, leiste, () => {
    willkommenZeigen();
    if (zustand.dokument && !wiederherstellen) einstellungSchreiben(`fenster.dok.${zustand.dokument.id}`, fensterverwaltung.anordnung());
  });

  // ---- Statuszeile ----
  const meldung = h('div', { class: 'feld meldung' }, 'Bereit');
  const dokFeld = h('div', { class: 'feld' });
  const modusFeld = h('div', { class: 'feld' });
  const standFeld = h('div', { class: 'feld' });
  const verbFeld = h('div', { class: 'feld', title: 'Verbindung für Aktualisierungen' }, h('span', { class: 'punkt' }), 'Live');
  const benFeld = h('div', { class: 'feld' });
  wurzel.append(h('footer', { class: 'statuszeile' }, meldung, dokFeld, modusFeld, standFeld, verbFeld, benFeld));

  let meldungZeit = 0;
  an('meldung', ({ text, art }) => {
    meldung.textContent = text;
    meldung.className = `feld meldung${art === 'fehler' ? ' fehler' : art === 'warnung' ? ' warnung' : ''}`;
    clearTimeout(meldungZeit);
    meldungZeit = setTimeout(() => { meldung.textContent = 'Bereit'; meldung.className = 'feld meldung'; }, art === 'fehler' ? 10000 : 6000);
  });
  an('verbindung', (ok) => { verbFeld.firstChild.className = `punkt ${ok ? 'verbunden' : zustand.dokument ? 'getrennt' : ''}`; });

  const standZeigen = () => {
    const d = zustand.dokument;
    const name = d ? d.name : '';
    titel.replaceChildren(document.createTextNode(`${name ? `${name}${d?.geaendert ? ' •' : ''} – ` : ''}${zustand.system?.anwendung?.replace(' (Beta)', '') || 'UntPlan Studio'}`), h('span', { class: 'beta' }, 'BETA'));
    document.title = `${name ? `${name} – ` : ''}UntPlan Studio (Beta)`;
    qaSpeichern.disabled = !d || !zustand.benutzer?.darfSchreiben;
    qaZurueck.disabled = !d?.kannRueckgaengig || !zustand.benutzer?.darfSchreiben;
    qaZurueck.title = d?.rueckgaengigText ? `Rückgängig: ${d.rueckgaengigText} (Strg+Z)` : 'Rückgängig (Strg+Z)';
    qaVor.disabled = !d?.kannWiederholen || !zustand.benutzer?.darfSchreiben;
    qaLaden.disabled = !d;
    dokFeld.textContent = d ? `${d.schulname || d.name} · ${d.periode}${d.periodeFixiert ? ' (fixiert)' : ''}` : 'Kein Dokument geöffnet';
    standFeld.textContent = !d ? '' : d.geaendert ? 'Ungespeicherte Änderungen (Autospeicherung)' : d.gespeichert ? `Gespeichert ${datumDe(d.gespeichert)}` : 'Gespeichert';
    modusFeld.textContent = zustand.modus === 'vertretung' ? 'Vertretungsmodus' : 'Stundenplanmodus';
  };
  an('stand', standZeigen);
  an('modus', standZeigen);
  const kontoZeigen = () => {
    const b = zustand.benutzer;
    benFeld.textContent = b ? `${b.anzeigename} (${b.rolle})` : '';
    leeren(konto);
    if (b) konto.append(h('span', null, b.anzeigename), h('button', { type: 'button', title: 'Konto', onclick: () => befehl('datei.backstage', { seite: 'konto' }) }, symbol('benutzer')));
  };
  kontoZeigen();
  standZeigen();

  // ---- Willkommen (leerer Arbeitsbereich) ----
  function willkommenZeigen() {
    const leer = fensterverwaltung.alle().length === 0;
    willkommen.hidden = !leer;
    if (!leer) return;
    leeren(willkommen);
    const d = zustand.dokument;
    willkommen.append(h('div', { class: 'karte' },
      h('h2', null, d ? d.name : 'Willkommen bei UntPlan Studio'),
      h('p', { class: 'leise' }, d ? 'Öffnen Sie Fenster über das Menüband – z. B. Dateneingabe → Lehrer oder Stundenpläne → Klassenplan.' : 'Öffnen Sie eine Schuldatei, legen Sie eine neue an oder laden Sie eine .up- bzw. BAL-Datei hoch.'),
      h('div', { class: 'knoepfe' }, d
        ? [h('button', { class: 'knopf', type: 'button', onclick: () => befehl('stammdaten.oeffnen', { typ: 'lehrer' }) }, symbol('lehrer'), 'Lehrkräfte'),
          h('button', { class: 'knopf', type: 'button', onclick: () => befehl('stammdaten.oeffnen', { typ: 'klasse' }) }, symbol('klassen'), 'Klassen'),
          h('button', { class: 'knopf', type: 'button', onclick: () => befehl('stundenplan.oeffnen', { typ: 'klasse' }) }, symbol('stundenplan'), 'Klassenplan')]
        : [h('button', { class: 'knopf primaer', type: 'button', onclick: () => befehl('datei.backstage', { seite: 'oeffnen' }) }, symbol('oeffnen'), 'Öffnen'),
          h('button', { class: 'knopf', type: 'button', onclick: () => befehl('datei.backstage', { seite: 'neu' }) }, symbol('neu'), 'Neu')])));
  }
  willkommenZeigen();

  // ---- Dokumentwechsel: Fensteranordnung des Dokuments wiederherstellen ----
  an('dokument', async (stand) => {
    wiederherstellen = true;
    try {
      fensterverwaltung.alleSchliessen();
      if (stand) {
        const liste = await einstellungLesen(`fenster.dok.${stand.id}`);
        if (zustand.dokument?.id === stand.id && Array.isArray(liste) && liste.length) fensterverwaltung.anordnungLaden(liste);
      }
    } finally {
      setTimeout(() => { wiederherstellen = false; }, 700);
    }
    willkommenZeigen();
    shell.ribbon.aktualisieren();
  });

  // ---- Tastatur ----
  document.addEventListener('keydown', (e) => {
    if (document.querySelector('.dialog-hintergrund, .anmeldung')) return;
    const imFeld = e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement || e.target instanceof HTMLSelectElement;
    const strg = e.ctrlKey || e.metaKey;
    if (strg && !e.shiftKey && e.key.toLowerCase() === 's') { e.preventDefault(); if (zustand.dokument) speichern().catch(fehlerMelden); return; }
    if (!imFeld && strg && !e.shiftKey && e.key.toLowerCase() === 'z') { e.preventDefault(); rueckgaengig().catch(fehlerMelden); return; }
    if (!imFeld && strg && (e.key.toLowerCase() === 'y' || (e.shiftKey && e.key.toLowerCase() === 'z'))) { e.preventDefault(); wiederholen().catch(fehlerMelden); return; }
    if (!imFeld) tasteBehandeln(e);
  });

  shell.ribbon.zeichnen();
  melden('stand', zustand.dokument);
}
