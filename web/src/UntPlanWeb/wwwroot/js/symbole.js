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

/** Bildmarke von UntPlanWeb (eigenes Logo, kein Fremdlogo): Zifferblatt aus Strichen mit orangen Zeigern. */
export const LOGO = `<svg class="logo" viewBox="0 0 48 48" aria-hidden="true" focusable="false">
  <g stroke="#8a96a1" stroke-width="2.2" stroke-linecap="round">${Array.from({ length: 12 }, (_, i) => {
    const w = i * Math.PI / 6, a = 17, b = i % 3 === 0 ? 22.5 : 21;
    return `<line x1="${(24 + a * Math.sin(w)).toFixed(2)}" y1="${(24 - a * Math.cos(w)).toFixed(2)}" x2="${(24 + b * Math.sin(w)).toFixed(2)}" y2="${(24 - b * Math.cos(w)).toFixed(2)}"/>`;
  }).join('')}</g>
  <path d="M24 24V12.5M24 24l7.5 5" stroke="#f56b2a" stroke-width="3.2" stroke-linecap="round" fill="none"/>
  <circle cx="24" cy="24" r="2.6" fill="#414d55"/>
</svg>`;

/** RSS-Kachel für „Nachrichten“ (Bootstrap Icon in Orange). */
export const RSS = '<i class="bi bi-rss-fill u-rss" aria-hidden="true"></i>';
