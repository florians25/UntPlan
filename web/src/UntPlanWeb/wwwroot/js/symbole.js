// Symbole für Navigation und Seiten: ausschließlich die Original-Icons aus dem Paket des Kollegen (img/wu, 28 × 28).
// Die Dateien werden als CSS-Maske gezeichnet, damit sie wie im Original grau und im aktiven Menüpunkt orange erscheinen.
// Kleine Bedienelemente ohne eigenes Original-Icon (Pfeile, Papierkorb …) kommen weiter aus Bootstrap Icons (ui.js).

const W = '/img/wu/';

/** Name → Icon-Datei unter img/wu. */
const S = {
  // Hauptmenü
  dashboard: 'navigation-dashboard.svg',
  uebersicht: 'overview.svg',
  mitteilungen: 'admin-messages.svg',
  meinplan: 'my_timetable.svg',
  stundenplan: 'navigation-timetable.svg',
  abwesenheiten: 'absences.svg',
  hausaufgaben: 'homework.svg',
  unterricht: 'navigation-lessons.svg',
  klassenbuchnav: 'navigation-class-register.svg',
  meldenbuchen: 'resources.svg',
  verwaltung: 'navigation-administration.svg',
  stammdaten: 'my_data.svg',
  meinedaten: 'my_data.svg',
  vertretungen: 'tasks.svg',

  // Stundenplan-Untermenü
  klasse: 'shared_classEntity.svg',
  lehrkraft: 'shared_teacherEntity.svg',
  raumplan: 'shared_roomEntity.svg',
  tagklassen: 'classes_daily.svg',
  taglehrer: 'teachers_daily.svg',
  tagraeume: 'rooms_daily.svg',
  ressource: 'resources.svg',

  // Klassenbuch
  woche: 'navigation-class-register.svg',
  offen: 'open_periods_teacher.svg',
  eintraege: 'class_register_entries.svg',
  klassendienste: 'class_services.svg',
  fehlzeiten: 'absences.svg',
  befreiungen: 'exemptions.svg',
  berichte: 'reports.svg',
  sitzplan: 'lessons_students.svg',

  // Mitteilungen
  posteingang: 'admin-messages.svg',
  gesendet: 'sent.svg',
  entwuerfe: 'draft.svg',
  verfassen: 'homework.svg',
  listen: 'lessons_students.svg',

  // Melden & Buchen, Unterricht
  abwesend: 'absences.svg',
  raum: 'shared_roomEntity.svg',
  klausur: 'examinations.svg',
  pruefung: 'exam_planner.svg',
  protokoll: 'reports.svg',
  meineunterrichte: 'my_lessons.svg',

  // Administration
  synchron: 'navigation-administration.svg',
  benutzer: 'my_data.svg',
  ruckmeldung: 'tasks.svg',
  nachrichten: 'admin-messages.svg',
  monitor: 'navigation-dashboard.svg',
  freietage: 'navigation-timetable.svg',
  import: 'reports.svg',
  export: 'reports.svg',
  aktive: 'lessons_students.svg',
  rechte: 'navigation-administration.svg',
  zuordnung: 'lessons_classes.svg',
  sperre: 'exam_planner.svg',
  ansicht: 'overview.svg',
  versionen: 'navigation-timetable.svg',

  // Allgemein
  zurueck: 'exact/navigation-back.svg',
  abmelden: 'log_out.svg',
  profil: 'my_data.svg',
  wechseln: 'my_data.svg',
  sitzplanRaum: 'lessons_students.svg',
};

/** Icon als HTML (Maske in currentColor); unbekannte Namen liefern null. */
export function symbol(name, klasse = '') {
  const datei = S[name];
  if (!datei) return null;
  return `<span class="u-sym wu ${klasse}" style="--wu:url(${W}${datei})" aria-hidden="true"></span>`;
}

/** Farbiges Original-Icon als Bild (z. B. Statuspunkte, Info, Hausaufgabe), Pfad relativ zu img/wu. */
export const bild = (datei, klasse = '', titel = '') => `<img class="wu-bild ${klasse}" src="${W}${datei}" alt="${titel}" ${titel ? `title="${titel}"` : 'aria-hidden="true"'}>`;

export const symbolNamen = Object.keys(S);

/** Logo von UntPlan als Kachel: „UP“ (ein U, dessen rechter Schaft in den Bogen eines P übergeht) weiß auf Anthrazit, die Punze
 *  des P als Zeitfenster in Terrakotta. Gleiche Geometrie wie img/logo.svg (erzeugt von build/logo.py). */
export const LOGO = `<svg class="logo" viewBox="0 0 64 64" aria-hidden="true" focusable="false">
  <path fill="#24323D" fill-rule="evenodd" d="M14 0L50 0A14 14 0 0 1 64 14L64 50A14 14 0 0 1 50 64L14 64A14 14 0 0 1 0 50L0 14A14 14 0 0 1 14 0Z"/>
  <path transform="translate(6.896 8.02) scale(0.749)" fill="#FFFFFF" fill-rule="evenodd" d="M6 26L6 41A17 17 0 0 0 23 58A17 17 0 0 0 40 41L40 37L45.5 37A15.5 15.5 0 0 0 61 21.5A15.5 15.5 0 0 0 45.5 6L29 6L29 41A6 6 0 0 1 23 47A6 6 0 0 1 17 41L17 26Z"/>
  <path transform="translate(6.896 8.02) scale(0.749)" fill="#B8452B" fill-rule="evenodd" d="M41.5 17L47.5 17A1.5 1.5 0 0 1 49 18.5L49 24.5A1.5 1.5 0 0 1 47.5 26L41.5 26A1.5 1.5 0 0 1 40 24.5L40 18.5A1.5 1.5 0 0 1 41.5 17Z"/>
</svg>`;

/** RSS-Kachel für „Nachrichten“ (Bootstrap Icon in Orange). */
export const RSS = '<i class="bi bi-rss-fill u-rss" aria-hidden="true"></i>';
