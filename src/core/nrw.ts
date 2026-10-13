// NRW-spezifische Stammdaten und Vorgaben.
// Quellen: Ferienordnung NRW (BASS 12-63 Nr. 1), VO zu § 93 Abs. 2 SchulG (Pflichtstunden),
// APO-SI, APO-GOSt. Alle Werte sind Vorgaben/Voreinstellungen und in der Software änderbar.

import { addDays, easterSunday } from './dates';
import type { FreierTag, Kursart, Schulform, Fach, RaumTyp } from './model';

export const SCHULFORMEN: Record<Schulform, string> = {
  GS: 'Grundschule',
  HS: 'Hauptschule',
  RS: 'Realschule',
  SK: 'Sekundarschule',
  GE: 'Gesamtschule',
  GY: 'Gymnasium',
  BK: 'Berufskolleg',
  FS: 'Förderschule',
  PS: 'PRIMUS-Schule',
  WB: 'Weiterbildungskolleg',
};

/** Wöchentliche Pflichtstunden einer vollzeitbeschäftigten Lehrkraft (VO zu § 93 Abs. 2 SchulG) */
export const PFLICHTSTUNDEN: Record<Schulform, number> = {
  GS: 28,
  HS: 28,
  RS: 28,
  SK: 25.5,
  GE: 25.5,
  GY: 25.5,
  BK: 25.5,
  FS: 27.5,
  PS: 25.5,
  WB: 25,
};

/** Jahrgangsstufen je Schulform */
export const JAHRGAENGE: Record<Schulform, string[]> = {
  GS: ['1', '2', '3', '4'],
  HS: ['5', '6', '7', '8', '9', '10'],
  RS: ['5', '6', '7', '8', '9', '10'],
  SK: ['5', '6', '7', '8', '9', '10'],
  GE: ['5', '6', '7', '8', '9', '10', 'EF', 'Q1', 'Q2'],
  GY: ['5', '6', '7', '8', '9', '10', 'EF', 'Q1', 'Q2'], // G9 (seit 2019/20)
  BK: ['Unterstufe', 'Mittelstufe', 'Oberstufe', 'FOS 11', 'FOS 12', 'BG 11', 'BG 12', 'BG 13'],
  FS: ['1', '2', '3', '4', '5', '6', '7', '8', '9', '10'],
  PS: ['1', '2', '3', '4', '5', '6', '7', '8', '9', '10'],
  WB: ['E', 'Q1', 'Q2'],
};

export const OBERSTUFE = ['EF', 'Q1', 'Q2'];

export function istOberstufe(jahrgang: string): boolean {
  return OBERSTUFE.includes(jahrgang);
}

/** Reihenfolge von Jahrgängen für Vergleiche ("ab EF") */
export function jahrgangRang(j: string): number {
  const n = Number(j);
  if (!Number.isNaN(n)) return n;
  const map: Record<string, number> = { EF: 11, Q1: 12, Q2: 13, E: 11 };
  return map[j] ?? 99;
}

/** Kursarten der gymnasialen Oberstufe (APO-GOSt) mit Regel-Wochenstunden */
export const KURSARTEN: Record<Kursart, { name: string; stunden: number }> = {
  GK: { name: 'Grundkurs', stunden: 3 },
  LK: { name: 'Leistungskurs', stunden: 5 },
  ZK: { name: 'Zusatzkurs', stunden: 3 },
  VTF: { name: 'Vertiefungskurs', stunden: 2 },
  PJK: { name: 'Projektkurs', stunden: 2 },
  AG: { name: 'Arbeitsgemeinschaft', stunden: 2 },
  WP: { name: 'Wahlpflicht', stunden: 3 },
  FÖ: { name: 'Förderunterricht', stunden: 1 },
};

/** Fächerkatalog mit in NRW (Schild-NRW/amtliche Schulstatistik) gebräuchlichen Kürzeln */
export const FAECHER_NRW: Array<Omit<Fach, 'id'>> = [
  f('D', 'Deutsch', '#e57373', null, true),
  f('M', 'Mathematik', '#64b5f6', null, true),
  f('E', 'Englisch', '#81c784', null, true),
  f('F', 'Französisch', '#4db6ac', null, true),
  f('L', 'Latein', '#a1887f', null, true),
  f('S', 'Spanisch', '#ffb74d', null, true),
  f('BI', 'Biologie', '#aed581', 'Fachraum'),
  f('CH', 'Chemie', '#ba68c8', 'Fachraum'),
  f('PH', 'Physik', '#7986cb', 'Fachraum'),
  f('IF', 'Informatik', '#4fc3f7', 'Computerraum'),
  f('EK', 'Erdkunde', '#dce775', null),
  f('GE', 'Geschichte', '#d7ccc8', null),
  f('PK', 'Politik', '#ffd54f', null),
  f('WI', 'Wirtschaft-Politik', '#ffca28', null),
  f('SW', 'Sozialwissenschaften', '#ffe082', null),
  f('PL', 'Philosophie', '#b0bec5', null),
  f('PPL', 'Praktische Philosophie', '#cfd8dc', null),
  f('KR', 'Kath. Religionslehre', '#ce93d8', null),
  f('ER', 'Ev. Religionslehre', '#b39ddb', null),
  f('IRU', 'Islamischer Religionsunterricht', '#9fa8da', null),
  f('KU', 'Kunst', '#f48fb1', 'Kunstraum'),
  f('MU', 'Musik', '#f06292', 'Musikraum'),
  f('SP', 'Sport', '#90a4ae', 'Sporthalle'),
  f('NW', 'Naturwissenschaften', '#a5d6a7', 'Fachraum'),
  f('GL', 'Gesellschaftslehre', '#e6ee9c', null),
  f('AL', 'Arbeitslehre', '#bcaaa4', null),
  f('TC', 'Technik', '#b0bec5', 'Fachraum'),
  f('SU', 'Sachunterricht', '#c5e1a5', null),
  f('KL', 'Klassenleitungsstunde', '#eeeeee', null),
  f('FÖ', 'Förderunterricht', '#e0e0e0', null),
  f('LZ', 'Lernzeit', '#f5f5f5', null),
];

function f(kuerzel: string, name: string, farbe: string, raumTyp: RaumTyp | null, hauptfach = false): Omit<Fach, 'id'> {
  return { kuerzel, name, farbe, raumTyp, hauptfach, maxProTag: 2 };
}

/**
 * Ferientermine NRW (Ferienordnung). Bewegliche Ferientage legt jede Schule selbst fest
 * und werden in UntPlan unter "Kalender" ergänzt.
 */
export const FERIEN_NRW: Array<Omit<FreierTag, 'id'>> = [
  { name: 'Herbstferien', von: '2025-10-13', bis: '2025-10-25', art: 'Ferien' },
  { name: 'Weihnachtsferien', von: '2025-12-22', bis: '2026-01-06', art: 'Ferien' },
  { name: 'Osterferien', von: '2026-03-30', bis: '2026-04-11', art: 'Ferien' },
  { name: 'Pfingstferien', von: '2026-05-26', bis: '2026-05-26', art: 'Ferien' },
  { name: 'Sommerferien', von: '2026-07-20', bis: '2026-09-01', art: 'Ferien' },
  { name: 'Herbstferien', von: '2026-10-17', bis: '2026-10-31', art: 'Ferien' },
  { name: 'Weihnachtsferien', von: '2026-12-23', bis: '2027-01-06', art: 'Ferien' },
  { name: 'Osterferien', von: '2027-03-22', bis: '2027-04-03', art: 'Ferien' },
  { name: 'Pfingstferien', von: '2027-05-18', bis: '2027-05-18', art: 'Ferien' },
  { name: 'Sommerferien', von: '2027-07-19', bis: '2027-08-31', art: 'Ferien' },
];

/** Gesetzliche Feiertage in Nordrhein-Westfalen für ein Kalenderjahr */
export function feiertageNRW(year: number): Array<Omit<FreierTag, 'id'>> {
  const ostern = easterSunday(year);
  const tag = (name: string, iso: string) => ({ name, von: iso, bis: iso, art: 'Feiertag' as const });
  return [
    tag('Neujahr', `${year}-01-01`),
    tag('Karfreitag', addDays(ostern, -2)),
    tag('Ostermontag', addDays(ostern, 1)),
    tag('Tag der Arbeit', `${year}-05-01`),
    tag('Christi Himmelfahrt', addDays(ostern, 39)),
    tag('Pfingstmontag', addDays(ostern, 50)),
    tag('Fronleichnam', addDays(ostern, 60)),
    tag('Tag der Deutschen Einheit', `${year}-10-03`),
    tag('Allerheiligen', `${year}-11-01`),
    tag('1. Weihnachtstag', `${year}-12-25`),
    tag('2. Weihnachtstag', `${year}-12-26`),
  ];
}

/** Standard-Zeitraster (45-Minuten-Stunden) */
export function standardZeitraster(): Array<{ nr: number; beginn: string; ende: string }> {
  return [
    { nr: 1, beginn: '07:55', ende: '08:40' },
    { nr: 2, beginn: '08:45', ende: '09:30' },
    { nr: 3, beginn: '09:50', ende: '10:35' },
    { nr: 4, beginn: '10:40', ende: '11:25' },
    { nr: 5, beginn: '11:45', ende: '12:30' },
    { nr: 6, beginn: '12:35', ende: '13:20' },
    { nr: 7, beginn: '13:25', ende: '14:10' },
    { nr: 8, beginn: '14:15', ende: '15:00' },
    { nr: 9, beginn: '15:05', ende: '15:50' },
    { nr: 10, beginn: '15:55', ende: '16:40' },
  ];
}

/** Prüft eine NRW-Schulnummer (6-stellig, numerisch) */
export function pruefeSchulnummer(nr: string): boolean {
  return /^\d{6}$/.test(nr.trim());
}

/** Bestimmt das Schuljahr (NRW: Beginn nach den Sommerferien, Ende 31.07.) für ein Datum */
export function schuljahrFuer(iso: string): string {
  const [y, m] = iso.split('-').map(Number);
  const start = m >= 8 ? y : y - 1;
  return `${start}/${String((start + 1) % 100).padStart(2, '0')}`;
}
