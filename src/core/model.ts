// UntPlan – Datenmodell
// Alle Daten einer Schule werden in einer einzigen Schuldatei (*.untplan, JSON) gespeichert.
// Das Format ist bewusst einfach gehalten, damit es später 1:1 mit dem Web-Portal
// synchronisiert werden kann (siehe docs/SYNC_API.md).

export const DATA_VERSION = 1;

export type ID = string;

/** Wochentag 0 = Montag … 5 = Samstag */
export type Tag = number;

export interface Slot {
  tag: Tag;
  stunde: number; // 1-basiert
}

export type Schulform =
  | 'GS' // Grundschule
  | 'HS' // Hauptschule
  | 'RS' // Realschule
  | 'SK' // Sekundarschule
  | 'GE' // Gesamtschule
  | 'GY' // Gymnasium
  | 'BK' // Berufskolleg
  | 'FS' // Förderschule
  | 'PS' // PRIMUS-Schule
  | 'WB'; // Weiterbildungskolleg

export interface SchoolMeta {
  schulname: string;
  schulnummer: string; // 6-stellige NRW-Schulnummer
  schulform: Schulform;
  ort: string;
  schuljahr: string; // z. B. "2026/27"
  schuljahrBeginn: string; // ISO-Datum
  schuljahrEnde: string; // ISO-Datum
  halbjahrWechsel: string; // ISO-Datum (Beginn 2. Halbjahr)
  bundesland: 'NW';
}

export interface Period {
  nr: number;
  beginn: string; // "08:00"
  ende: string; // "08:45"
}

export interface Zeitraster {
  tage: number; // 5 = Mo–Fr, 6 = Mo–Sa
  stunden: Period[];
  /** Mittagspause: in dieser Stunde wird möglichst kein Unterricht geplant (0 = keine) */
  mittagspauseStunde: number;
}

export interface Anrechnung {
  grund: string; // z. B. "Schulleitung", "Altersermäßigung", "Fachleitung"
  stunden: number;
}

export interface Lehrer {
  id: ID;
  kuerzel: string;
  nachname: string;
  vorname: string;
  email: string;
  pflichtstunden: number; // Vollzeit-Deputat bzw. individuelles Deputat (Teilzeit)
  anrechnungen: Anrechnung[];
  faecher: ID[]; // Lehrbefähigungen
  sperrzeiten: Slot[]; // nicht einsetzbar
  maxStundenProTag: number;
  /** Präsenz-/Bereitschaftsstunden für Vertretungen */
  bereitschaft: Slot[];
  aktiv: boolean;
  notiz: string;
}

export interface Klasse {
  id: ID;
  name: string; // "5a", "EF", "Q1"
  jahrgang: string; // "5" … "13", "EF", "Q1", "Q2"
  klassenlehrerIds: ID[];
  raumId: ID | null; // Stammraum
  schuelerzahl: number;
  sperrzeiten: Slot[];
  /** Springstunden in diesem Klassenplan vermeiden (Sek I: Pflicht) */
  keineHohlstunden: boolean;
}

export type RaumTyp = 'Klassenraum' | 'Fachraum' | 'Sporthalle' | 'Computerraum' | 'Musikraum' | 'Kunstraum' | 'Sonstiges';

export interface Raum {
  id: ID;
  kuerzel: string;
  name: string;
  typ: RaumTyp;
  kapazitaet: number;
  sperrzeiten: Slot[];
}

export interface Fach {
  id: ID;
  kuerzel: string;
  name: string;
  farbe: string;
  raumTyp: RaumTyp | null; // benötigter Fachraum
  /** Hauptfach – wird bevorzugt in den ersten Stunden geplant */
  hauptfach: boolean;
  /** Max. Stunden pro Tag für eine Klasse */
  maxProTag: number;
}

export type Kursart = 'GK' | 'LK' | 'ZK' | 'VTF' | 'PJK' | 'AG' | 'WP' | 'FÖ';

/** Unterricht = eine Zeile der Unterrichtsverteilung (Lehrereinsatz) */
export interface Unterricht {
  id: ID;
  fachId: ID;
  lehrerIds: ID[]; // Teamteaching möglich
  klassenIds: ID[]; // Klassenkopplung möglich (z. B. Religion 5a/5b)
  wochenstunden: number;
  /** Anzahl gewünschter Doppelstunden (je 2 Stunden) */
  doppelstunden: number;
  raumIds: ID[]; // erlaubte Räume (leer = Stammraum / beliebig passend)
  gruppe: string; // Teilgruppe, z. B. "Gr. 1", "KR"
  kursart: Kursart | null;
  kursname: string; // z. B. "D-LK1"
  /** Unterricht in derselben Schiene (Band/Block) wird parallel gelegt */
  schiene: string;
  /** Halbjahr 0 = ganzjährig, 1 = nur 1. HJ, 2 = nur 2. HJ */
  halbjahr: 0 | 1 | 2;
  notiz: string;
}

export interface Planeintrag {
  id: ID;
  unterrichtId: ID;
  tag: Tag;
  stunde: number;
  raumId: ID | null;
  fixiert: boolean;
}

export interface Aufsichtsbereich {
  id: ID;
  name: string;
  /** Anzahl benötigter Aufsichten je Pause */
  bedarf: number;
}

export interface Pause {
  id: ID;
  name: string;
  nachStunde: number; // Pause liegt nach dieser Stunde
  dauer: number; // Minuten
}

export interface Aufsicht {
  id: ID;
  bereichId: ID;
  pauseId: ID;
  tag: Tag;
  lehrerId: ID;
}

export type AbwesenheitsTyp = 'lehrer' | 'klasse' | 'raum';

export type AbwesenheitsGrund =
  | 'Krankheit'
  | 'Fortbildung'
  | 'Dienstlich'
  | 'Klassenfahrt'
  | 'Exkursion'
  | 'Praktikum'
  | 'Prüfung'
  | 'Sonderurlaub'
  | 'Mutterschutz/Elternzeit'
  | 'Sperrung'
  | 'Sonstiges';

export interface Abwesenheit {
  id: ID;
  typ: AbwesenheitsTyp;
  refId: ID;
  von: string; // ISO-Datum
  bis: string; // ISO-Datum (inklusive)
  stundeVon: number; // 1 … n (ganzer Tag: 1 bis max)
  stundeBis: number;
  grund: AbwesenheitsGrund;
  text: string;
  /** Begleitende Lehrkräfte (z. B. bei Klassenfahrten) */
  begleitLehrerIds: ID[];
}

export type VertretungsArt =
  | 'Vertretung'
  | 'Entfall'
  | 'Raumänderung'
  | 'Verlegung'
  | 'Mitbetreuung'
  | 'EVA' // Eigenverantwortliches Arbeiten
  | 'Sondereinsatz'
  | 'Freisetzung'; // Unterricht entfällt, weil Klasse abwesend

export interface Vertretung {
  id: ID;
  datum: string; // ISO-Datum
  stunde: number;
  /** Ursprünglicher Planeintrag (null bei Sondereinsätzen) */
  planeintragId: ID | null;
  unterrichtId: ID | null;
  art: VertretungsArt;
  vertreterIds: ID[];
  raumId: ID | null;
  fachId: ID | null;
  /** Bei Verlegung: Zieltermin */
  nachDatum: string | null;
  nachStunde: number | null;
  text: string;
  /** Zählt als Mehrarbeit (Vertretung außerhalb des eigenen Plans) */
  mehrarbeit: boolean;
  erstellt: string; // ISO-Zeitstempel
  geaendert: string;
}

export type FerienArt = 'Ferien' | 'Feiertag' | 'Beweglicher Ferientag' | 'Unterrichtsfrei' | 'Pädagogischer Tag';

export interface FreierTag {
  id: ID;
  name: string;
  von: string;
  bis: string;
  art: FerienArt;
}

export interface Tagesnachricht {
  id: ID;
  datum: string;
  text: string;
  /** Nur für Lehrkräfte sichtbar */
  intern: boolean;
}

export interface StundentafelZeile {
  jahrgang: string;
  fachId: ID;
  stunden: number;
}

export interface SyncSettings {
  portalUrl: string;
  schulId: string;
  apiKey: string;
  autoSync: boolean;
  /** Anzahl Tage, für die Vertretungen veröffentlicht werden */
  tageVoraus: number;
  letzteSynchronisation: string | null;
  letzteRevision: number;
}

export interface Einstellungen {
  /** Ab dieser Anzahl Mehrarbeitsstunden im Kalendermonat wird vergütet (NRW: mehr als 3) */
  mehrarbeitsGrenze: number;
  /** Entfall statt Vertretung erlauben ab Jahrgang (z. B. "EF") */
  entfallErlaubtAbJahrgang: string;
  maxVertretungenProWoche: number;
  vertretungsplanTitel: string;
}

export interface SchoolData {
  dataVersion: number;
  meta: SchoolMeta;
  zeitraster: Zeitraster;
  lehrer: Lehrer[];
  klassen: Klasse[];
  raeume: Raum[];
  faecher: Fach[];
  unterricht: Unterricht[];
  stundenplan: Planeintrag[];
  pausen: Pause[];
  aufsichtsbereiche: Aufsichtsbereich[];
  aufsichten: Aufsicht[];
  abwesenheiten: Abwesenheit[];
  vertretungen: Vertretung[];
  freieTage: FreierTag[];
  tagesnachrichten: Tagesnachricht[];
  stundentafel: StundentafelZeile[];
  sync: SyncSettings;
  einstellungen: Einstellungen;
}

export const TAGE_KURZ = ['Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'];
export const TAGE_LANG = ['Montag', 'Dienstag', 'Mittwoch', 'Donnerstag', 'Freitag', 'Samstag'];

let idCounter = 0;
export function newId(prefix = 'id'): ID {
  idCounter = (idCounter + 1) % 1_000_000;
  const rnd = Math.random().toString(36).slice(2, 8);
  return `${prefix}_${Date.now().toString(36)}${idCounter.toString(36)}${rnd}`;
}

export function slotKey(tag: Tag, stunde: number): string {
  return `${tag}:${stunde}`;
}

export function hasSlot(list: Slot[], tag: Tag, stunde: number): boolean {
  return list.some((s) => s.tag === tag && s.stunde === stunde);
}

export function lehrerName(l: Lehrer | undefined): string {
  if (!l) return '?';
  return `${l.nachname}${l.vorname ? ', ' + l.vorname : ''}`;
}

/** Soll-Stunden (Unterrichtsverpflichtung abzüglich Anrechnungen) */
export function sollStunden(l: Lehrer): number {
  return l.pflichtstunden - l.anrechnungen.reduce((s, a) => s + a.stunden, 0);
}
