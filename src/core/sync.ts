// Web-Portal-Synchronisation – Datenformat (Schema "untplan-sync/1").
// Die Desktop-Anwendung ist die führende Quelle ("Master") für Stunden- und Vertretungsplan.
// Das Portal veröffentlicht die Daten (Schüler/Eltern/Lehrkräfte) und sammelt Meldungen ein
// (z. B. Krankmeldungen von Lehrkräften), die in der Desktop-Anwendung übernommen werden.
// Vollständige API-Beschreibung: docs/SYNC_API.md

export const SYNC_SCHEMA = 'untplan-sync/1';

export interface SyncPlanEintrag {
  tag: number;
  stunde: number;
  fach: string;
  klassen: string[];
  lehrer: string[];
  raum: string;
  gruppe: string;
}

export interface SyncVertretung {
  datum: string;
  stunde: number;
  art: string;
  klassen: string[];
  fach: string;
  lehrer: string[];
  vertreter: string[];
  raum: string;
  text: string;
}

export interface SyncPayload {
  schema: typeof SYNC_SCHEMA;
  erzeugt: string;
  basisRevision: number;
  schule: { name: string; schulnummer: string; schulform: string; schuljahr: string; ort: string };
  zeitraster: { tage: number; stunden: { nr: number; beginn: string; ende: string }[] };
  lehrer: { kuerzel: string; name: string; email: string }[];
  klassen: { name: string; jahrgang: string; klassenleitung: string[] }[];
  raeume: { kuerzel: string; name: string }[];
  faecher: { kuerzel: string; name: string; farbe: string }[];
  stundenplan: SyncPlanEintrag[];
  vertretungen: SyncVertretung[];
  nachrichten: { datum: string; text: string; intern: boolean }[];
  freieTage: { name: string; von: string; bis: string; art: string }[];
}

/** Meldungen aus dem Portal an die Desktop-Anwendung */
export interface PortalMeldung {
  id: string;
  typ: 'abwesenheit' | 'nachricht';
  erstellt: string;
  absender: string; // Lehrerkürzel oder Benutzername
  /** Bei Abwesenheiten */
  lehrerKuerzel?: string;
  von?: string;
  bis?: string;
  stundeVon?: number;
  stundeBis?: number;
  grund?: string;
  text?: string;
}

export interface SyncAntwort {
  ok: boolean;
  revision: number;
  meldung?: string;
}

export interface SyncRequest {
  portalUrl: string;
  schulId: string;
  apiKey: string;
}
