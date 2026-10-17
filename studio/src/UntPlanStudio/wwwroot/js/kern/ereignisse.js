// Ereignisbus der Oberfläche (zwischen Shell, Fenstern und Modulen).
//
// Wichtige Ereignisse:
//   'dokument'   – anderes Dokument geöffnet bzw. geschlossen (Daten: Stand oder null)
//   'stand'      – Stand des Dokuments aktualisiert (Titel, Rückgängig, geändert …)
//   'daten'      – Daten geändert: { version, bereich: 'Stammdaten'|'Unterricht'|'Stundenplan'|'Vertretung'|'Einstellungen'|'Alles', quelle, benutzer }
//   'modus'      – Stundenplan-/Vertretungsmodus gewechselt ('stundenplan'|'vertretung')
//   'element'    – Element in einem Fenster gewählt { typ, id, quelle } (Fensterverknüpfung wie im Desktop)
//   'meldung'    – Text für die Statuszeile { text, art }
//   'abgemeldet' – Sitzung abgelaufen

const hoerer = new Map();

/** Meldet sich für ein Ereignis an; liefert eine Funktion zum Abmelden. */
export function an(name, f) {
  if (!hoerer.has(name)) hoerer.set(name, new Set());
  hoerer.get(name).add(f);
  return () => hoerer.get(name)?.delete(f);
}

export function melden(name, daten) {
  for (const f of [...(hoerer.get(name) || [])]) {
    try { f(daten); } catch (e) { console.error(`Fehler im Ereignis ${name}`, e); }
  }
}

/** Betrifft eine Datenänderung die genannten Bereiche? ('Alles' betrifft immer alles.) */
export function betrifft(ereignis, ...bereiche) {
  return !ereignis?.bereich || ereignis.bereich === 'Alles' || bereiche.includes(ereignis.bereich);
}
