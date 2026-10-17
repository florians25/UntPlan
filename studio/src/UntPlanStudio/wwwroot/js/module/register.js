// Zentrale Liste aller Oberflächen-Module – die EINZIGE Stelle, an der ein neues Modul eingetragen wird.
// Reihenfolge = Ladereihenfolge. geplant.js zuerst: es legt Platzhalter im Menüband an, die echte Module über die
// gleiche Eintrags-id ersetzen (geplant.js muss dafür nicht geändert werden). Siehe ARCHITEKTUR.md.

import geplant from './geplant.js';
import datei from './datei.js';
import start from './start.js';
import stammdaten from './stammdaten.js';
import unterricht from './unterricht.js';
import stundenplan from './stundenplan.js';
// ── ERWEITERUNGSSTELLE: weitere Module hier importieren (je Modul eine Zeile, alphabetisch), z. B.
// import abrechnung from './abrechnung.js';
// import druck from './druck.js';
// import vertretung from './vertretung.js';

export const module = [
  geplant,
  datei,
  start,
  stammdaten,
  unterricht,
  stundenplan,
  // ── ERWEITERUNGSSTELLE: und hier anfügen
];
