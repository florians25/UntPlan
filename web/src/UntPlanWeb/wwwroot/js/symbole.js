// Eigene Linien-Symbole für Navigation und Seiten (selbst gezeichnet, 28 × 28, Strichstärke 1,5, Farbe über currentColor).
// Die Motive folgen der gewohnten Bildsprache von Stundenplanprogrammen; es werden keine fremden Grafiken verwendet.

const K = (x, y, r) => `<circle cx="${x}" cy="${y}" r="${r}"/>`;
const P = d => `<path d="${d}"/>`;
const R = (x, y, b, h, r = 1.5) => `<rect x="${x}" y="${y}" width="${b}" height="${h}" rx="${r}"/>`;
const PUNKT = (x, y) => `<circle cx="${x}" cy="${y}" r="1" fill="currentColor" stroke="none"/>`;

/** Kalenderblatt (Grundform für Stundenplan-Symbole), optional kleiner für Zusätze. */
const kalender = (b = 22, h = 20) => R(3, 5, b, h) + P(`M3 10.5h${b}M8.5 3v4M${3 + b - 5.5} 3v4`);
const kalenderKlein = () => R(2.5, 4.5, 16, 15) + P('M2.5 9h16M6.5 2.5v3.5M14.5 2.5v3.5');
const person = (x, y, s = 1) => K(x, y, 2.6 * s) + P(`M${x - 4.6 * s} ${y + 9 * s}c0-3 2-5 ${4.6 * s}-5s${4.6 * s} 2 ${4.6 * s} 5`);
const dokument = () => P('M7 3h9.5L22 8.5V25H7z') + P('M16.5 3v5.5H22');

const S = {
  // Hauptmenü
  dashboard: R(3.5, 3.5, 9, 12) + R(3.5, 18.5, 9, 6) + R(15.5, 3.5, 9, 6) + R(15.5, 12.5, 9, 12),
  uebersicht: '<ellipse cx="12" cy="7" rx="8.5" ry="3.5"/>' + P('M3.5 7v6c0 1.9 3.8 3.5 8.5 3.5M3.5 13v6c0 1.9 3.8 3.5 8.5 3.5M20.5 7v4') + K(21, 17, 2.4) + P('M16.6 25c0-2.6 2-4.2 4.4-4.2s4.4 1.6 4.4 4.2'),
  mitteilungen: R(3, 6.5, 22, 15, 1.5) + P('M3.5 7.5 14 15.5l10.5-8'),
  meinplan: kalender() + P('m14 13.2 1.45 2.95 3.25.47-2.35 2.3.55 3.24L14 20.6l-2.9 1.56.55-3.24-2.35-2.3 3.25-.47z'),
  stundenplan: kalender() + PUNKT(8.5, 15) + PUNKT(14, 15) + PUNKT(19.5, 15) + PUNKT(8.5, 20) + PUNKT(14, 20) + PUNKT(19.5, 20),
  abwesenheiten: P('M8 3.5h12v9H8zM6.5 12.5h15v3.5h-15zM8 16v8.5M20 16v8.5M8 20.5h12'),
  hausaufgaben: P('M7 3h9.5L21 7.5V14') + P('M7 3v22h7') + P('M16.5 3v4.5H21') + P('M10 10h7M10 13.5h5') + P('M15.5 19.5l5-4.2 5 4.2M17 18.5v6.5h7v-6.5'),
  unterricht: R(4, 4, 20, 14, 1) + P('M14 18v4M9 25l5-3 5 3M8 9h8M8 12.5h5') + P('m18 8 3 3'),
  klassenbuchnav: P('M6 4.5h14.5a1.5 1.5 0 0 1 1.5 1.5v17a1.5 1.5 0 0 1-1.5 1.5H6z') + P('M9.5 4.5v20M13 4.5v7l2.25-1.6 2.25 1.6v-7'),
  meldenbuchen: R(6, 5, 16, 20, 1.5) + P('M10.5 3h7v4h-7zM14 11.5v8M10 15.5h8'),
  verwaltung: K(14, 14, 3.4) + P('M14 3.5v3M14 21.5v3M3.5 14h3M21.5 14h3M6.6 6.6l2.1 2.1M19.3 19.3l2.1 2.1M6.6 21.4l2.1-2.1M19.3 8.7l2.1-2.1') + K(14, 14, 7.6),
  stammdaten: '<ellipse cx="14" cy="6.5" rx="9" ry="3.5"/>' + P('M5 6.5v15c0 1.9 4 3.5 9 3.5s9-1.6 9-3.5v-15M5 11.5c0 1.9 4 3.5 9 3.5s9-1.6 9-3.5M5 16.5c0 1.9 4 3.5 9 3.5s9-1.6 9-3.5'),
  vertretungen: P('M4 10h17M17 5.5 21.5 10 17 14.5M24 18H7M11 13.5 6.5 18l4.5 4.5'),

  // Stundenplan-Untermenü
  klasse: R(13.5, 3.5, 11, 8.5, 1) + P('M16.5 9.5v-2M19 9.5V6M21.5 9.5V7.5') + person(8, 6.5, .8) + person(7.5, 17.5, .75) + person(17.5, 17.5, .75),
  lehrkraft: person(10, 6.8, .95) + R(14.5, 15.5, 10.5, 8.5, 1.2) + P('M17.5 15.5v-1.8h4.5v1.8M14.5 19.5h10.5'),
  raumplan: P('M7 25V4h14v21M4 25h20') + P('M7 4l8 2.5v18.5L7 25') + PUNKT(12.5, 15),
  tagklassen: kalenderKlein() + person(21, 17, .62) + person(15.8, 19.5, .55),
  taglehrer: kalenderKlein() + R(15, 17, 10, 8, 1.2) + P('M18 17v-1.7h4V17M15 20.5h10'),
  tagraeume: kalenderKlein() + P('M17 25.5v-10h7.5v10M15.5 25.5h10.5M17 15.5l4.2 1.4v8.6') + PUNKT(20, 21),
  ressource: R(5, 8, 18, 15, 1.5) + P('M5 13h18M11.5 17.5h5M9 8l2-4h6l2 4'),

  // Klassenbuch
  woche: dokument() + K(13.5, 16, 3.6) + P('m16.2 18.7 3.3 3.3'),
  offen: dokument() + R(13, 15, 11, 10, 1.2) + P('M13 18.5h11M16 13.5v2.8M21 13.5v2.8'),
  eintraege: dokument() + K(19.5, 20.5, 4.5) + P('M19.5 18.3v4.4M17.3 20.5h4.4'),
  klassendienste: person(17, 7.5, .9) + P('M3 22h6.5l3 2.5h7.5a1.6 1.6 0 0 0 0-3.2h-5') + P('M3 18.5h4.5l3.5 3'),
  fehlzeiten: K(14, 14, 10) + P('M14 8v6.5l4.5 3'),
  befreiungen: dokument() + K(19.5, 20.5, 4.5) + P('m16.3 23.7 6.4-6.4'),
  berichte: R(3.5, 5, 21, 18, 1.5) + K(10.5, 14, 3.6) + P('M10.5 10.4v3.6h3.6M17 11h5M17 14h5M17 17h3.5'),
  sitzplan: R(4, 18, 8, 6, 1) + R(16, 18, 8, 6, 1) + R(4, 8, 8, 6, 1) + R(16, 8, 8, 6, 1) + P('M9 3.5h10'),

  // Mitteilungen
  posteingang: P('M3.5 15.5 7 5h14l3.5 10.5V23a1.5 1.5 0 0 1-1.5 1.5H5A1.5 1.5 0 0 1 3.5 23z') + P('M3.5 15.5h6l1.5 3h6l1.5-3h6'),
  gesendet: P('M25 3.5 3.5 12.5l8 3.5 3.5 8z') + P('M25 3.5 11.5 16'),
  entwuerfe: dokument() + P('m13 22 1-4 7-7 3 3-7 7z'),
  listen: person(9, 6.5, .75) + person(17, 7.5, .65) + R(13.5, 16, 11.5, 8.5, 1.2) + P('m14 16.8 5.25 4 5.25-4'),

  // Melden & Buchen, Unterricht
  abwesend: person(11, 7, .95) + P('m18.5 15.5 6 6M24.5 15.5l-6 6'),
  raum: P('M6 25V4h13v8M3.5 25H14') + P('M6 4l7 2.2V25') + PUNKT(10.5, 15) + K(20.5, 19.5, 5) + P('M20.5 17v5M18 19.5h5'),
  klausur: dokument() + P('M10.5 12.5h8M10.5 16h8M10.5 19.5h4.5'),
  pruefung: dokument() + P('m10.5 16.5 2.6 2.6 5.4-5.6'),
  protokoll: P('M10 7h14M10 14h14M10 21h14') + PUNKT(5, 7) + PUNKT(5, 14) + PUNKT(5, 21),
  meineunterrichte: R(4, 4, 20, 14, 1) + P('M14 18v4M9 25l5-3 5 3') + person(14, 9.2, .6),

  // Administration
  synchron: P('M22.5 11A9 9 0 0 0 6 8.5M5.5 17a9 9 0 0 0 16.5 2.5') + P('M6 3.5v5h5M22 24.5v-5h-5'),
  benutzer: person(10.5, 7.5, .95) + person(19.5, 8.5, .75) + P('M17 21h8'),
  ruckmeldung: P('M3.5 15.5 7 5h14l3.5 10.5V23a1.5 1.5 0 0 1-1.5 1.5H5A1.5 1.5 0 0 1 3.5 23z') + P('M3.5 15.5h6l1.5 3h6l1.5-3h6M14 8v6M11.5 11.5 14 14l2.5-2.5'),
  nachrichten: P('M4 11.5v5h3.5l9 5.5v-16l-9 5.5z') + P('M7.5 16.5 9 23h3l-1-6M20 10.5a5 5 0 0 1 0 7M22.5 8a8.5 8.5 0 0 1 0 12'),
  monitor: R(3.5, 4.5, 21, 14.5, 1.5) + P('M10 24h8M14 19v5'),
  freietage: kalender() + K(14, 17.5, 3) + P('M14 12.5v1.2M14 21.3v1.2M9 17.5h1.2M17.8 17.5H19'),
  import: P('M14 3.5v13M9 12l5 5 5-5') + P('M4 18.5v4A2 2 0 0 0 6 24.5h16a2 2 0 0 0 2-2v-4'),
  export: P('M14 17V4M9 8.5l5-5 5 5') + P('M4 18.5v4A2 2 0 0 0 6 24.5h16a2 2 0 0 0 2-2v-4'),
  aktive: person(11, 7, .95) + P('m17.5 19 2.5 2.5 5-5.5'),
  rechte: P('M14 3.5 5 7v6.5c0 5.5 3.8 9.6 9 11 5.2-1.4 9-5.5 9-11V7z') + K(14, 12.5, 2.5) + P('M14 15v5'),
  zuordnung: person(7.5, 6, .7) + person(20.5, 6, .7) + R(9.5, 18, 9, 6.5, 1.2) + P('M7.5 15.5v2.5h2M20.5 15.5v2.5h-2'),
  sperre: kalender() + R(10.5, 16, 7, 6, 1) + P('M11.8 16v-1.8a2.2 2.2 0 0 1 4.4 0V16'),
  ansicht: P('M3 14s4-7.5 11-7.5S25 14 25 14s-4 7.5-11 7.5S3 14 3 14z') + K(14, 14, 3.5),
  versionen: P('m14 4 10 5-10 5L4 9z') + P('m4 14 10 5 10-5M4 19l10 5 10-5'),

  // Allgemein
  zurueck: P('M23 14H5.5M12 7.5 5.5 14l6.5 6.5'),
  abmelden: P('M15 4.5H5.5v19H15') + P('M11 14h13M19.5 9.5 24 14l-4.5 4.5'),
  profil: person(14, 9, 1.2),
  hilfe: K(14, 14, 10.5) + P('M10.8 11a3.3 3.3 0 1 1 4.6 3c-.9.4-1.4 1.1-1.4 2v.8') + PUNKT(14, 20),
  einklappen: P('M17.5 7.5 11 14l6.5 6.5'),
  menue: P('M5 8h18M5 14h18M5 20h18'),
  drucken: P('M8 9V4h12v5') + R(4, 9, 20, 10, 1.5) + P('M8 15h12v9H8z'),
  wechseln: person(9.5, 7, .8) + P('M17 13.5h7.5M21.5 10.5l3 3-3 3M24.5 21H17M20 18l-3 3 3 3'),
  sitzplanRaum: R(4, 4, 20, 20, 1.5),
};

/** SVG-Symbol als HTML; unbekannte Namen liefern null. */
export function symbol(name, klasse = '') {
  const inhalt = S[name];
  if (!inhalt) return null;
  return `<svg class="u-sym ${klasse}" viewBox="0 0 28 28" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">${inhalt}</svg>`;
}

export const symbolNamen = Object.keys(S);

/** Eigene Bildmarke: Zifferblatt aus Strichen (grau) mit orangen Zeigern – „Stunden im Blick“. */
export const LOGO = `<svg class="logo" viewBox="0 0 48 48" aria-hidden="true" focusable="false">
  <g stroke="#8a96a1" stroke-width="2.2" stroke-linecap="round">${Array.from({ length: 12 }, (_, i) => {
    const w = i * Math.PI / 6, a = 17, b = i % 3 === 0 ? 22.5 : 21;
    return `<line x1="${(24 + a * Math.sin(w)).toFixed(2)}" y1="${(24 - a * Math.cos(w)).toFixed(2)}" x2="${(24 + b * Math.sin(w)).toFixed(2)}" y2="${(24 - b * Math.cos(w)).toFixed(2)}"/>`;
  }).join('')}</g>
  <path d="M24 24V12.5M24 24l7.5 5" stroke="#f56b2a" stroke-width="3.2" stroke-linecap="round" fill="none"/>
  <circle cx="24" cy="24" r="2.6" fill="#414d55"/>
</svg>`;

/** RSS-Symbol (orange Kachel) für „Nachrichten“. */
export const RSS = `<svg class="u-rss" viewBox="0 0 16 16" aria-hidden="true" focusable="false"><rect width="16" height="16" rx="3" fill="#f56b2a"/>
  <circle cx="4.6" cy="11.4" r="1.4" fill="#fff"/><path d="M3.4 7.3a5.3 5.3 0 0 1 5.3 5.3M3.4 3.6a9 9 0 0 1 9 9" stroke="#fff" stroke-width="1.7" fill="none" stroke-linecap="round"/></svg>`;
