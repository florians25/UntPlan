// Eigene, schlichte Symbole (SVG, 24×24) – zweifarbig: Linie (.l) und Fläche (.f, .f2, .f3).
// Neue Symbole einfach in SYMBOLE ergänzen; unbekannte Namen zeigen ein neutrales Quadrat.

const SYMBOLE = {
  neu: '<path class="f" d="M6 3h8l4 4v14H6z"/><path class="l" d="M6 3h8l4 4v14H6zM14 3v4h4"/><path class="l a" d="M12 11v6M9 14h6"/>',
  oeffnen: '<path class="f2" d="M3 6h6l2 2h10v11H3z"/><path class="l" d="M3 6h6l2 2h10v11H3z"/><path class="f" d="M5 11h17l-2 8H3z"/><path class="l" d="M5 11h17l-2 8H3z"/>',
  speichern: '<path class="f" d="M4 4h13l3 3v13H4z"/><path class="l" d="M4 4h13l3 3v13H4z"/><path class="f2 l" d="M8 4h8v5H8zM7 13h10v7H7z"/>',
  herunterladen: '<path class="l" d="M12 4v11M7 10l5 5 5-5"/><path class="l a" d="M4 17v3h16v-3"/>',
  hochladen: '<path class="l" d="M12 15V4M7 9l5-5 5 5"/><path class="l a" d="M4 17v3h16v-3"/>',
  rueckgaengig: '<path class="l" d="M9 7H5V3"/><path class="l" d="M5 7a8 8 0 1 1-1 7"/>',
  wiederholen: '<path class="l" d="M15 7h4V3"/><path class="l" d="M19 7a8 8 0 1 0 1 7"/>',
  schliessen: '<path class="l" d="M6 6l12 12M18 6L6 18"/>',
  minimieren: '<path class="l" d="M5 18h14"/>',
  maximieren: '<path class="l" d="M5 5h14v14H5z"/>',
  wiederherstellen: '<path class="l" d="M8 8h11v11H8zM5 16V5h11"/>',
  plus: '<path class="l" d="M12 5v14M5 12h14"/>',
  loeschen: '<path class="f3" d="M7 7h10l-1 13H8z"/><path class="l" d="M5 7h14M10 7V4h4v3M7 7l1 13h8l1-13"/>',
  suchen: '<circle class="f l" cx="10" cy="10" r="6"/><path class="l" d="M15 15l5 5"/>',
  klassen: '<circle class="f" cx="8" cy="8" r="3"/><circle class="f2" cx="16" cy="8" r="3"/><path class="l" d="M8 5a3 3 0 1 1 0 6 3 3 0 0 1 0-6zM16 5a3 3 0 1 1 0 6 3 3 0 0 1 0-6z"/><path class="f l" d="M2 20c0-4 3-6 6-6s6 2 6 6z"/><path class="f2 l" d="M10 20c0-4 3-6 6-6s6 2 6 6z"/>',
  lehrer: '<circle class="f" cx="12" cy="7" r="4"/><path class="l" d="M12 3a4 4 0 1 1 0 8 4 4 0 0 1 0-8z"/><path class="f2 l" d="M4 21c0-5 4-8 8-8s8 3 8 8z"/><path class="l a" d="M12 13v4"/>',
  raeume: '<path class="f2" d="M3 10l9-6 9 6v10H3z"/><path class="l" d="M3 10l9-6 9 6v10H3z"/><path class="f l" d="M9 20v-6h6v6"/>',
  faecher: '<path class="f" d="M4 5h7v15H4z"/><path class="f2" d="M13 5h7v15h-7z"/><path class="l" d="M4 5h7v15H4zM13 5h7v15h-7zM6 9h3M15 9h3"/>',
  unterricht: '<path class="f" d="M3 5h18v12H3z"/><path class="l" d="M3 5h18v12H3zM8 21h8M12 17v4"/><path class="l a" d="M7 9h6M7 12h10"/>',
  stundenplan: '<path class="f" d="M3 4h18v17H3z"/><path class="l" d="M3 4h18v17H3zM3 9h18M9 4v17M15 4v17"/><path class="f2" d="M10 10h4v5h-4z"/>',
  uebersicht: '<path class="f" d="M3 4h18v16H3z"/><path class="l" d="M3 4h18v16H3zM3 9h18M3 14h18M8 4v16"/>',
  nebeneinander: '<path class="f" d="M3 4h8v16H3z"/><path class="f2" d="M13 4h8v16h-8z"/><path class="l" d="M3 4h8v16H3zM13 4h8v16h-8z"/>',
  untereinander: '<path class="f" d="M3 3h18v8H3z"/><path class="f2" d="M3 13h18v8H3z"/><path class="l" d="M3 3h18v8H3zM3 13h18v8H3z"/>',
  ueberlappend: '<path class="f2 l" d="M8 8h13v12H8z"/><path class="f l" d="M3 3h13v12H3z"/>',
  fenster: '<path class="f" d="M3 4h18v16H3z"/><path class="l" d="M3 4h18v16H3zM3 8h18"/>',
  anordnung: '<path class="f" d="M3 4h10v9H3z"/><path class="f2" d="M15 4h6v16h-6zM3 15h10v5H3z"/><path class="l" d="M3 4h10v9H3zM15 4h6v16h-6zM3 15h10v5H3z"/>',
  import: '<path class="f" d="M4 3h10l4 4v14H4z"/><path class="l" d="M4 3h10l4 4v14H4z"/><path class="l a" d="M22 13H10M13 10l-3 3 3 3"/>',
  export: '<path class="f" d="M4 3h10l4 4v14H4z"/><path class="l" d="M4 3h10l4 4v14H4z"/><path class="l a" d="M9 13h13M19 10l3 3-3 3"/>',
  drucken: '<path class="f2" d="M7 3h10v5H7z"/><path class="f" d="M3 8h18v9H3z"/><path class="l" d="M7 8V3h10v5M3 8h18v9H3zM7 14h10v7H7z"/>',
  einstellungen: '<circle class="f l" cx="12" cy="12" r="3.5"/><path class="l" d="M12 2v3M12 19v3M2 12h3M19 12h3M5 5l2 2M17 17l2 2M19 5l-2 2M7 17l-2 2"/>',
  benutzer: '<circle class="f l" cx="12" cy="8" r="4"/><path class="f2 l" d="M4 21c0-5 4-7 8-7s8 2 8 7z"/>',
  abmelden: '<path class="l" d="M14 4H5v16h9"/><path class="l a" d="M10 12h11M18 9l3 3-3 3"/>',
  vertretung: '<circle class="f" cx="8" cy="8" r="3"/><circle class="f2" cx="16" cy="8" r="3"/><path class="l" d="M8 5a3 3 0 1 1 0 6 3 3 0 0 1 0-6zM16 5a3 3 0 1 1 0 6 3 3 0 0 1 0-6z"/><path class="l a" d="M5 16h14M16 13l3 3-3 3"/>',
  kalender: '<path class="f" d="M3 5h18v16H3z"/><path class="l" d="M3 5h18v16H3zM3 10h18M8 3v4M16 3v4"/><path class="f2" d="M7 13h4v4H7z"/>',
  optimierung: '<path class="f" d="M12 2l3 6 6 1-4.5 4 1 6-5.5-3-5.5 3 1-6L3 9l6-1z"/><path class="l" d="M12 2l3 6 6 1-4.5 4 1 6-5.5-3-5.5 3 1-6L3 9l6-1z"/>',
  diagnose: '<path class="f" d="M4 4h16v16H4z"/><path class="l" d="M4 4h16v16H4z"/><path class="l a" d="M6 14l3-4 3 5 3-8 3 7"/>',
  zeitwunsch: '<circle class="f l" cx="12" cy="13" r="8"/><path class="l a" d="M12 8v5l3 2"/><path class="l" d="M9 2h6"/>',
  perioden: '<path class="f" d="M3 6h8v12H3z"/><path class="f2" d="M13 6h8v12h-8z"/><path class="l" d="M3 6h8v12H3zM13 6h8v12h-8z"/>',
  abteilungen: '<path class="f" d="M9 3h6v5H9z"/><path class="f2" d="M3 16h6v5H3zM15 16h6v5h-6z"/><path class="l" d="M9 3h6v5H9zM3 16h6v5H3zM15 16h6v5h-6zM12 8v4M6 16v-4h12v4"/>',
  gaenge: '<path class="f" d="M3 18l6-12h6l6 12z"/><path class="l" d="M3 18l6-12h6l6 12zM12 8v2M12 13v2"/>',
  schueler: '<circle class="f l" cx="12" cy="8" r="3.5"/><path class="f2 l" d="M5 20c0-4 3-6 7-6s7 2 7 6z"/><path class="l a" d="M3 6l9-3 9 3"/>',
  abwesenheit: '<circle class="f l" cx="12" cy="12" r="8"/><path class="l a" d="M6 6l12 12"/>',
  info: '<circle class="f l" cx="12" cy="12" r="9"/><path class="l a" d="M12 11v6M12 7v1"/>',
  hilfe: '<circle class="f l" cx="12" cy="12" r="9"/><path class="l a" d="M9.5 9.5a2.5 2.5 0 1 1 3.5 2.3c-.7.3-1 1-1 1.7M12 17v.5"/>',
  web: '<circle class="f l" cx="12" cy="12" r="9"/><path class="l" d="M3 12h18M12 3c3 3 3 15 0 18M12 3c-3 3-3 15 0 18"/>',
  datenbank: '<ellipse class="f l" cx="12" cy="6" rx="7" ry="3"/><path class="f2 l" d="M5 6v12c0 1.7 3 3 7 3s7-1.3 7-3V6"/><path class="l" d="M5 12c0 1.7 3 3 7 3s7-1.3 7-3"/>',
  statistik: '<path class="f" d="M5 13h3v7H5z"/><path class="f2" d="M10.5 8h3v12h-3z"/><path class="f" d="M16 4h3v16h-3z"/><path class="l" d="M5 13h3v7H5zM10.5 8h3v12h-3zM16 4h3v16h-3zM3 20h18"/>',
  veranstaltung: '<path class="f" d="M3 5h18v16H3z"/><path class="l" d="M3 5h18v16H3zM3 10h18M8 3v4M16 3v4"/><path class="l a" d="M12 12l1.2 2.5 2.8.4-2 2 .5 2.8-2.5-1.3-2.5 1.3.5-2.8-2-2 2.8-.4z"/>',
  pausenaufsicht: '<circle class="f l" cx="12" cy="12" r="4"/><path class="l" d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z"/>',
  fixieren: '<path class="f" d="M9 3h6l-1 6 3 3H7l3-3z"/><path class="l" d="M9 3h6l-1 6 3 3H7l3-3zM12 12v9"/>',
  tausch: '<path class="l" d="M4 8h14M15 5l3 3-3 3"/><path class="l a" d="M20 16H6M9 13l-3 3 3 3"/>',
  kopieren: '<path class="f2 l" d="M8 8h12v12H8z"/><path class="f l" d="M4 4h12v12H4z"/>',
  spalten: '<path class="f" d="M3 4h18v16H3z"/><path class="l" d="M3 4h18v16H3zM9 4v16M15 4v16"/>',
  sortieren: '<path class="l" d="M7 4v16M4 17l3 3 3-3"/><path class="l a" d="M17 20V4M14 7l3-3 3 3"/>',
  zeile_neu: '<path class="f" d="M3 6h18v4H3z"/><path class="l" d="M3 6h18v4H3zM3 14h8"/><path class="l a" d="M17 13v8M13 17h8"/>',
  zeile_loeschen: '<path class="f3" d="M3 6h18v4H3z"/><path class="l" d="M3 6h18v4H3zM3 14h8"/><path class="l a" d="M14 15l6 6M20 15l-6 6"/>',
  verknuepfen: '<path class="l" d="M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1"/><path class="l a" d="M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1"/>',
  bal: '<path class="f" d="M4 3h12l4 4v14H4z"/><path class="l" d="M4 3h12l4 4v14H4z"/><path class="l a" d="M8 11h8M8 14h8M8 17h5"/>',
  modul: '<path class="f" d="M4 4h7v7H4z"/><path class="f2" d="M13 4h7v7h-7zM4 13h7v7H4z"/><path class="f" d="M13 13h7v7h-7z"/><path class="l" d="M4 4h7v7H4zM13 4h7v7h-7zM4 13h7v7H4zM13 13h7v7h-7z"/>',
  beispiel: '<path class="f" d="M4 4h16v16H4z"/><path class="l" d="M4 4h16v16H4z"/><path class="l a" d="M8 12l3 3 5-6"/>',
  menue: '<path class="l" d="M4 7h16M4 12h16M4 17h16"/>',
  pfeil_runter: '<path class="l" d="M6 9l6 6 6-6"/>',
  logo: '<rect x="2" y="2" width="20" height="20" rx="4" fill="#fff" fill-opacity=".95"/><path d="M6 7h12v11H6z" fill="none" stroke="#125e7a" stroke-width="1.6"/><path d="M6 10.5h12M10 7v11M14 7v11" stroke="#125e7a" stroke-width="1.2"/><path d="M10 10.5h4v4h-4z" fill="#60bae8"/>',
};

const NEUTRAL = '<path class="f l" d="M5 5h14v14H5z"/>';

/** SVG-Symbol als Markup-Text (für innerHTML eigener Elemente). */
export function symbolSvg(name, klasse = '') {
  return `<svg class="sym ${klasse}" viewBox="0 0 24 24" aria-hidden="true">${SYMBOLE[name] || NEUTRAL}</svg>`;
}

/** SVG-Symbol als Element. */
export function symbol(name, klasse = '') {
  const t = document.createElement('template');
  t.innerHTML = symbolSvg(name, klasse);
  return t.content.firstElementChild;
}

export const symbolNamen = () => Object.keys(SYMBOLE);
