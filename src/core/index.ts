// Nachschlage-Strukturen für schnellen Zugriff auf Stammdaten.

import type { Fach, Klasse, Lehrer, Planeintrag, Raum, SchoolData, Unterricht } from './model';

export interface DataIndex {
  lehrer: Map<string, Lehrer>;
  klassen: Map<string, Klasse>;
  raeume: Map<string, Raum>;
  faecher: Map<string, Fach>;
  unterricht: Map<string, Unterricht>;
  planeintraege: Map<string, Planeintrag>;
  /** Planeinträge je Unterricht */
  plaeneProUnterricht: Map<string, Planeintrag[]>;
}

export function buildIndex(d: SchoolData): DataIndex {
  const byId = <T extends { id: string }>(arr: T[]) => new Map(arr.map((x) => [x.id, x] as const));
  const plaeneProUnterricht = new Map<string, Planeintrag[]>();
  for (const p of d.stundenplan) {
    const list = plaeneProUnterricht.get(p.unterrichtId);
    if (list) list.push(p);
    else plaeneProUnterricht.set(p.unterrichtId, [p]);
  }
  return {
    lehrer: byId(d.lehrer),
    klassen: byId(d.klassen),
    raeume: byId(d.raeume),
    faecher: byId(d.faecher),
    unterricht: byId(d.unterricht),
    planeintraege: byId(d.stundenplan),
    plaeneProUnterricht,
  };
}

export type Sicht = 'klasse' | 'lehrer' | 'raum';

/** Liefert die Planeinträge, die für Klasse/Lehrer/Raum relevant sind */
export function eintraegeFuer(d: SchoolData, idx: DataIndex, sicht: Sicht, refId: string): Planeintrag[] {
  return d.stundenplan.filter((p) => {
    const u = idx.unterricht.get(p.unterrichtId);
    if (!u) return false;
    if (sicht === 'klasse') return u.klassenIds.includes(refId);
    if (sicht === 'lehrer') return u.lehrerIds.includes(refId);
    return p.raumId === refId;
  });
}

/** Ist-Stunden einer Lehrkraft laut Unterrichtsverteilung (bei Teamteaching volle Stunden) */
export function istStundenLehrer(d: SchoolData, lehrerId: string, halbjahr: 1 | 2 = 1): number {
  return d.unterricht
    .filter((u) => u.lehrerIds.includes(lehrerId) && (u.halbjahr === 0 || u.halbjahr === halbjahr))
    .reduce((s, u) => s + u.wochenstunden, 0);
}

export function kurzLehrer(idx: DataIndex, ids: string[]): string {
  return ids.map((id) => idx.lehrer.get(id)?.kuerzel ?? '?').join(', ');
}

export function kurzKlassen(idx: DataIndex, ids: string[]): string {
  return ids.map((id) => idx.klassen.get(id)?.name ?? '?').join(', ');
}

export function unterrichtLabel(idx: DataIndex, u: Unterricht): string {
  const fach = idx.faecher.get(u.fachId)?.kuerzel ?? '?';
  const kurs = u.kursname ? ` (${u.kursname})` : u.gruppe ? ` (${u.gruppe})` : '';
  return `${fach}${kurs} – ${kurzKlassen(idx, u.klassenIds)} – ${kurzLehrer(idx, u.lehrerIds)}`;
}
