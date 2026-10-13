// Pausenaufsichten: automatische, faire Verteilung und Vertretung bei Abwesenheit.

import { buildIndex } from './index';
import { hasSlot, newId, sollStunden, type Aufsicht, type SchoolData } from './model';
import { lehrerAbwesend } from './vertretung';
import { weekdayIndex } from './dates';

/** Ist die Lehrkraft an dem Tag vor oder nach der Pause im Haus? */
function imHaus(d: SchoolData, stundenProTag: Map<string, Set<number>>, lehrerId: string, tag: number, nachStunde: number): boolean {
  const s = stundenProTag.get(`${lehrerId}:${tag}`);
  if (!s) return false;
  return s.has(nachStunde) || s.has(nachStunde + 1);
}

export function verteileAufsichten(d: SchoolData): Aufsicht[] {
  const idx = buildIndex(d);
  const stundenProTag = new Map<string, Set<number>>();
  for (const p of d.stundenplan) {
    const u = idx.unterricht.get(p.unterrichtId);
    if (!u) continue;
    for (const l of u.lehrerIds) {
      const k = `${l}:${p.tag}`;
      if (!stundenProTag.has(k)) stundenProTag.set(k, new Set());
      stundenProTag.get(k)!.add(p.stunde);
    }
  }
  const count = new Map<string, number>();
  const result: Aufsicht[] = [];
  const aktive = d.lehrer.filter((l) => l.aktiv);
  for (let tag = 0; tag < d.zeitraster.tage; tag++) {
    for (const pause of d.pausen) {
      const amTag = new Set<string>();
      for (const b of d.aufsichtsbereiche) {
        for (let n = 0; n < b.bedarf; n++) {
          const cand = aktive
            .filter((l) => !amTag.has(l.id) && imHaus(d, stundenProTag, l.id, tag, pause.nachStunde))
            .filter((l) => !hasSlot(l.sperrzeiten, tag, pause.nachStunde) && !hasSlot(l.sperrzeiten, tag, pause.nachStunde + 1))
            .sort((a, b) => (count.get(a.id) ?? 0) / Math.max(1, sollStunden(a)) - (count.get(b.id) ?? 0) / Math.max(1, sollStunden(b)));
          const l = cand[0];
          if (!l) continue;
          amTag.add(l.id);
          count.set(l.id, (count.get(l.id) ?? 0) + 1);
          result.push({ id: newId('au'), bereichId: b.id, pauseId: pause.id, tag, lehrerId: l.id });
        }
      }
    }
  }
  return result;
}

/** Aufsichten eines Datums, deren Lehrkraft abwesend ist */
export function offeneAufsichten(d: SchoolData, iso: string): Aufsicht[] {
  const wd = weekdayIndex(iso);
  return d.aufsichten.filter((a) => {
    if (a.tag !== wd) return false;
    const pause = d.pausen.find((p) => p.id === a.pauseId);
    return !!pause && !!lehrerAbwesend(d, a.lehrerId, iso, pause.nachStunde);
  });
}

export function aufsichtenProLehrer(d: SchoolData): Map<string, number> {
  const m = new Map<string, number>();
  for (const a of d.aufsichten) m.set(a.lehrerId, (m.get(a.lehrerId) ?? 0) + 1);
  return m;
}
