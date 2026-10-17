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

/** Bildmarke von UntPlanWeb: Stundenplan-Raster als reine Linienzeichnung (Strich 1,5 im 28er-Raster, runde Enden) wie die übrigen
 *  Linien-Icons, grau über currentColor (Farbe in app.css: .logo). Gleiches Motiv wie img/logo.svg (Favicon). */
export const LOGO = `<svg class="logo" viewBox="0 0 28 28" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">
  <rect x="3.25" y="5.25" width="21.5" height="19.5" rx="3"/>
  <path d="M9 3v4.5M19 3v4.5M3.25 10.5h21.5"/>
  <rect x="6.75" y="13.5" width="3.5" height="3" rx=".75"/>
  <rect x="6.75" y="18.5" width="3.5" height="2.75" rx=".75"/>
  <rect x="12.25" y="13.5" width="3.5" height="7.75" rx=".75"/>
  <rect x="17.75" y="13.5" width="3.5" height="4.5" rx=".75"/>
</svg>`;

/** RSS-Kachel für „Nachrichten“ (Bootstrap Icon in Orange). */
export const RSS = '<i class="bi bi-rss-fill u-rss" aria-hidden="true"></i>';
