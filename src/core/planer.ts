// Automatische Stundenplanerstellung ("Optimierung").
//
// Verfahren: Der Unterricht wird in Planungsblöcke zerlegt (Einzel-/Doppelstunden, parallele
// Schienen/Bänder werden zu einem Block zusammengefasst). Danach werden die Blöcke
// gierig (schwierigste zuerst) gesetzt und anschließend per Simulated Annealing optimiert.
// Harte Bedingungen (Kollisionen, Sperrzeiten, Raumkapazitäten) werden sehr hoch bestraft,
// weiche Bedingungen (Hohlstunden, Fachverteilung, Tagesbelastung, Randstunden) moderat.

import { buildIndex } from './index';
import { istOberstufe } from './nrw';
import { hasSlot, newId, type Planeintrag, type SchoolData, type Unterricht } from './model';

export interface PlanerOptionen {
  iterationen: number;
  seed: number;
  /** Bereits gesetzte, nicht fixierte Einträge als Startlösung behalten */
  bestehendeBehalten: boolean;
  /** Nur diese Klassen neu planen (leer = alle) */
  nurKlassenIds: string[];
  /** Fortschritts-Rückmeldung (0..1) */
  onProgress?: (anteil: number, kosten: number) => void;
}

export interface PlanerErgebnis {
  stundenplan: Planeintrag[];
  harteKonflikte: number;
  kosten: number;
  bloecke: number;
  ohneRaum: number;
  dauerMs: number;
}

export const STANDARD_OPTIONEN: PlanerOptionen = {
  iterationen: 150_000,
  seed: 42,
  bestehendeBehalten: false,
  nurKlassenIds: [],
};

const HART = 1000;

interface Block {
  members: Unterricht[];
  len: number;
  teachers: number[];
  classes: { k: number; gruppe: string }[];
  pools: number[];
  fachIds: string[];
  locked: boolean;
  tag: number;
  stunde: number;
  /** Bei gesperrten (fixierten) Blöcken: zugrundeliegender Planeintrag */
  fixedEntry?: Planeintrag;
}

function mulberry32(a: number) {
  return () => {
    a |= 0;
    a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

export function erstelleStundenplan(d: SchoolData, opt: Partial<PlanerOptionen> = {}): PlanerErgebnis {
  const o = { ...STANDARD_OPTIONEN, ...opt };
  const t0 = Date.now();
  const rand = mulberry32(o.seed);
  const idx = buildIndex(d);
  const TAGE = d.zeitraster.tage;
  const S = d.zeitraster.stunden.length;
  const mittag = d.zeitraster.mittagspauseStunde;

  const teacherIdx = new Map(d.lehrer.map((l, i) => [l.id, i] as const));
  const classIdx = new Map(d.klassen.map((k, i) => [k.id, i] as const));
  const nT = d.lehrer.length;
  const nE = nT + d.klassen.length; // Entitäten: erst Lehrkräfte, dann Klassen

  const nurKlassen = new Set(o.nurKlassenIds);
  const betroffen = (u: Unterricht) => nurKlassen.size === 0 || u.klassenIds.some((k) => nurKlassen.has(k));

  // ---- Raumpools: Unterricht mit festgelegten/typisierten Räumen konkurriert um begrenzte Räume
  const poolKeys = new Map<string, number>();
  const poolCap: number[] = [];
  const poolRooms: string[][] = [];
  const raeumeFuer = (u: Unterricht): string[] => {
    if (u.raumIds.length) return u.raumIds;
    const fach = idx.faecher.get(u.fachId);
    if (fach?.raumTyp) return d.raeume.filter((r) => r.typ === fach.raumTyp).map((r) => r.id);
    return [];
  };
  const poolOf = (u: Unterricht): number[] => {
    const rooms = raeumeFuer(u);
    if (!rooms.length) return [];
    const key = [...rooms].sort().join('|');
    let p = poolKeys.get(key);
    if (p === undefined) {
      p = poolCap.length;
      poolKeys.set(key, p);
      poolCap.push(rooms.length);
      poolRooms.push(rooms);
    }
    return [p];
  };

  // ---- Blöcke bilden
  const blocks: Block[] = [];
  const keepEntries: Planeintrag[] = []; // Einträge, die unverändert übernommen werden (andere Klassen)
  const fixedCount = new Map<string, number>();
  const startPos = new Map<string, Planeintrag[]>();

  for (const p of d.stundenplan) {
    const u = idx.unterricht.get(p.unterrichtId);
    if (!u) continue;
    if (!betroffen(u)) {
      keepEntries.push(p);
      continue;
    }
    if (p.fixiert) {
      fixedCount.set(u.id, (fixedCount.get(u.id) ?? 0) + 1);
      blocks.push(makeBlock([u], 1, true, p.tag, p.stunde, p));
    } else if (o.bestehendeBehalten) {
      const l = startPos.get(u.id);
      if (l) l.push(p);
      else startPos.set(u.id, [p]);
    }
  }
  // Nicht betroffene Einträge belegen Ressourcen, werden aber nicht bewegt
  for (const p of keepEntries) {
    const u = idx.unterricht.get(p.unterrichtId)!;
    blocks.push(makeBlock([u], 1, true, p.tag, p.stunde, p));
  }

  function makeBlock(members: Unterricht[], len: number, locked: boolean, tag = -1, stunde = -1, fixedEntry?: Planeintrag): Block {
    const teachers = new Set<number>();
    const classes: { k: number; gruppe: string }[] = [];
    const seenK = new Set<number>();
    const pools: number[] = [];
    for (const u of members) {
      for (const l of u.lehrerIds) {
        const i = teacherIdx.get(l);
        if (i !== undefined) teachers.add(i);
      }
      for (const k of u.klassenIds) {
        const i = classIdx.get(k);
        if (i === undefined || seenK.has(i)) continue;
        seenK.add(i);
        classes.push({ k: nT + i, gruppe: members.length > 1 ? '' : u.gruppe });
      }
      pools.push(...poolOf(u));
    }
    return { members, len, teachers: [...teachers], classes, pools, fachIds: members.map((m) => m.fachId), locked, tag, stunde, fixedEntry };
  }

  const zuPlanen = d.unterricht.filter((u) => betroffen(u) && u.wochenstunden > 0);
  const schienen = new Map<string, Unterricht[]>();
  const einzeln: Unterricht[] = [];
  for (const u of zuPlanen) {
    if (u.schiene) {
      const l = schienen.get(u.schiene);
      if (l) l.push(u);
      else schienen.set(u.schiene, [u]);
    } else einzeln.push(u);
  }
  const rest = (u: Unterricht) => Math.max(0, u.wochenstunden - (fixedCount.get(u.id) ?? 0));
  const addBlocks = (members: Unterricht[]) => {
    const r = Math.max(...members.map(rest));
    if (r <= 0) return;
    const doubles = Math.min(Math.min(...members.map((m) => m.doppelstunden)), Math.floor(r / 2));
    let placed = 0;
    for (let i = 0; i < doubles; i++, placed += 2) {
      const active = members.filter((m) => rest(m) > placed + 1);
      blocks.push(makeBlock(active, 2, false));
    }
    for (; placed < r; placed++) {
      const active = members.filter((m) => rest(m) > placed);
      blocks.push(makeBlock(active, 1, false));
    }
  };
  for (const u of einzeln) addBlocks([u]);
  for (const members of schienen.values()) addBlocks(members);

  // ---- Belegungsraster
  // grid[e][tag*S + s] = Liste der Blockindizes
  const grid: number[][][] = Array.from({ length: nE }, () => Array.from({ length: TAGE * S }, () => []));
  const poolGrid: number[][] = poolCap.map(() => new Array(TAGE * S).fill(0));

  const sperr: boolean[][] = Array.from({ length: nE }, (_, e) => {
    const arr = new Array(TAGE * S).fill(false);
    const list = e < nT ? d.lehrer[e].sperrzeiten : d.klassen[e - nT].sperrzeiten;
    for (let t = 0; t < TAGE; t++) for (let s = 1; s <= S; s++) if (hasSlot(list, t, s)) arr[t * S + s - 1] = true;
    return arr;
  });
  const maxTag = d.lehrer.map((l) => l.maxStundenProTag || S);
  const keineHohl = d.klassen.map((k) => k.keineHohlstunden);
  const oberstufe = d.klassen.map((k) => istOberstufe(k.jahrgang));
  const fachMax = new Map(d.faecher.map((f) => [f.id, f.maxProTag || 2] as const));
  const hauptfach = new Map(d.faecher.map((f) => [f.id, f.hauptfach] as const));

  function place(bi: number, sign: 1 | -1) {
    const b = blocks[bi];
    for (let k = 0; k < b.len; k++) {
      const cell = b.tag * S + b.stunde - 1 + k;
      for (const e of b.teachers) mutate(grid[e][cell], bi, sign);
      for (const c of b.classes) mutate(grid[c.k][cell], bi, sign);
      for (const p of b.pools) poolGrid[p][cell] += sign;
    }
  }
  function mutate(list: number[], bi: number, sign: 1 | -1) {
    if (sign > 0) list.push(bi);
    else {
      const i = list.lastIndexOf(bi);
      if (i >= 0) list.splice(i, 1);
    }
  }

  function entityDayCost(e: number, tag: number): number {
    let cost = 0;
    const base = tag * S;
    let first = -1;
    let last = -1;
    let count = 0;
    const isTeacher = e < nT;
    const fachCount = isTeacher ? null : new Map<string, number>();
    for (let s = 0; s < S; s++) {
      const list = grid[e][base + s];
      const n = list.length;
      if (n === 0) continue;
      count++;
      if (first < 0) first = s;
      last = s;
      if (n > 1) {
        if (isTeacher) cost += HART * (n - 1);
        else {
          // Teilgruppen dürfen parallel liegen
          const gruppen = new Set<string>();
          let ok = true;
          for (const bi of list) {
            const g = blocks[bi].classes.find((c) => c.k === e)?.gruppe ?? '';
            if (!g || gruppen.has(g)) ok = false;
            gruppen.add(g);
          }
          if (!ok) cost += HART * (n - 1);
        }
      }
      if (sperr[e][base + s]) cost += HART * n;
      if (!isTeacher) {
        const ki = e - nT;
        const stunde = s + 1;
        if (!oberstufe[ki] && stunde > 6) cost += (stunde - 6) * 3;
        if (stunde === mittag) cost += 8;
        for (const bi of list)
          for (const f of blocks[bi].fachIds) {
            fachCount!.set(f, (fachCount!.get(f) ?? 0) + 1);
            if (hauptfach.get(f) && stunde >= 6) cost += 2;
          }
      }
    }
    if (count > 0) {
      let gaps = 0;
      for (let s = first; s <= last; s++) if (grid[e][base + s].length === 0 && s + 1 !== mittag) gaps++;
      if (isTeacher) {
        cost += gaps * 3;
        if (count > maxTag[e]) cost += (count - maxTag[e]) * 15;
      } else {
        cost += gaps * (keineHohl[e - nT] ? 40 : 3);
        for (const [f, c] of fachCount!) {
          const m = fachMax.get(f) ?? 2;
          if (c > m) cost += (c - m) * 25;
        }
      }
    }
    return cost;
  }

  function poolDayCost(p: number, tag: number): number {
    let cost = 0;
    for (let s = 0; s < S; s++) {
      const over = poolGrid[p][tag * S + s] - poolCap[p];
      if (over > 0) cost += HART * over;
    }
    return cost;
  }

  function localCost(bis: number[], tage: number[]): number {
    const ents = new Set<number>();
    const pools = new Set<number>();
    for (const bi of bis) {
      const b = blocks[bi];
      for (const e of b.teachers) ents.add(e);
      for (const c of b.classes) ents.add(c.k);
      for (const p of b.pools) pools.add(p);
    }
    let c = 0;
    const days = [...new Set(tage)];
    for (const e of ents) for (const t of days) c += entityDayCost(e, t);
    for (const p of pools) for (const t of days) c += poolDayCost(p, t);
    return c;
  }

  function totalCost(): number {
    let c = 0;
    for (let e = 0; e < nE; e++) for (let t = 0; t < TAGE; t++) c += entityDayCost(e, t);
    for (let p = 0; p < poolCap.length; p++) for (let t = 0; t < TAGE; t++) c += poolDayCost(p, t);
    return c;
  }

  function hardCount(): number {
    let h = 0;
    for (let e = 0; e < nE; e++)
      for (let cell = 0; cell < TAGE * S; cell++) {
        const list = grid[e][cell];
        if (list.length === 0) continue;
        if (sperr[e][cell]) h += list.length;
        if (list.length > 1) {
          if (e < nT) h += list.length - 1;
          else {
            const gruppen = new Set<string>();
            let ok = true;
            for (const bi of list) {
              const g = blocks[bi].classes.find((c) => c.k === e)?.gruppe ?? '';
              if (!g || gruppen.has(g)) ok = false;
              gruppen.add(g);
            }
            if (!ok) h += list.length - 1;
          }
        }
      }
    for (let p = 0; p < poolCap.length; p++) for (const v of poolGrid[p]) if (v > poolCap[p]) h += v - poolCap[p];
    return h;
  }

  // ---- Startlösung
  for (let bi = 0; bi < blocks.length; bi++) if (blocks[bi].locked) place(bi, 1);

  const movable = blocks.map((b, i) => (b.locked ? -1 : i)).filter((i) => i >= 0);
  // Startpositionen aus bestehendem Plan übernehmen
  if (o.bestehendeBehalten) {
    for (const bi of movable) {
      const b = blocks[bi];
      const list = startPos.get(b.members[0].id);
      if (!list?.length) continue;
      const p = list.shift()!;
      if (b.len === 2) {
        const j = list.findIndex((q) => q.tag === p.tag && q.stunde === p.stunde + 1);
        if (j < 0 || p.stunde + 1 > S) {
          list.unshift(p);
          continue;
        }
        list.splice(j, 1);
      }
      b.tag = p.tag;
      b.stunde = p.stunde;
      place(bi, 1);
    }
  }

  const order = movable
    .filter((bi) => blocks[bi].tag < 0)
    .sort((a, b) => difficulty(blocks[b]) - difficulty(blocks[a]));
  function difficulty(b: Block) {
    return b.len * 10 + b.teachers.length * 3 + b.classes.length * 3 + b.members.length * 4 + b.pools.length * 5;
  }

  for (const bi of order) {
    const b = blocks[bi];
    let best = Infinity;
    let bestPos: [number, number][] = [];
    for (let t = 0; t < TAGE; t++) {
      for (let s = 1; s + b.len - 1 <= S; s++) {
        const before = localCost([bi], [t]);
        b.tag = t;
        b.stunde = s;
        place(bi, 1);
        const after = localCost([bi], [t]);
        place(bi, -1);
        const delta = after - before;
        if (delta < best - 1e-9) {
          best = delta;
          bestPos = [[t, s]];
        } else if (Math.abs(delta - best) < 1e-9) bestPos.push([t, s]);
      }
    }
    const [t, s] = bestPos[Math.floor(rand() * bestPos.length)] ?? [0, 1];
    b.tag = t;
    b.stunde = s;
    place(bi, 1);
  }

  // ---- Optimierung (Simulated Annealing)
  let cost = totalCost();
  let bestCost = cost;
  let bestState = movable.map((bi) => [blocks[bi].tag, blocks[bi].stunde] as const);
  const iters = movable.length > 0 ? o.iterationen : 0;
  const T0 = 60;
  const T1 = 0.05;
  const reportEvery = Math.max(1, Math.floor(iters / 50));

  for (let it = 0; it < iters; it++) {
    const T = T0 * Math.pow(T1 / T0, it / iters);
    const bi = movable[Math.floor(rand() * movable.length)];
    const b = blocks[bi];
    if (rand() < 0.6) {
      // Verschieben
      const nt = Math.floor(rand() * TAGE);
      const ns = 1 + Math.floor(rand() * (S - b.len + 1));
      if (nt === b.tag && ns === b.stunde) continue;
      const ot = b.tag;
      const os = b.stunde;
      const before = localCost([bi], [ot, nt]);
      place(bi, -1);
      b.tag = nt;
      b.stunde = ns;
      place(bi, 1);
      const delta = localCost([bi], [ot, nt]) - before;
      if (delta <= 0 || rand() < Math.exp(-delta / T)) cost += delta;
      else {
        place(bi, -1);
        b.tag = ot;
        b.stunde = os;
        place(bi, 1);
      }
    } else {
      // Tauschen mit Block gleicher Länge
      const bj = movable[Math.floor(rand() * movable.length)];
      const c = blocks[bj];
      if (bj === bi || c.len !== b.len || (c.tag === b.tag && c.stunde === b.stunde)) continue;
      const days = [b.tag, c.tag];
      const before = localCost([bi, bj], days);
      place(bi, -1);
      place(bj, -1);
      [b.tag, c.tag] = [c.tag, b.tag];
      [b.stunde, c.stunde] = [c.stunde, b.stunde];
      place(bi, 1);
      place(bj, 1);
      const delta = localCost([bi, bj], days) - before;
      if (delta <= 0 || rand() < Math.exp(-delta / T)) cost += delta;
      else {
        place(bi, -1);
        place(bj, -1);
        [b.tag, c.tag] = [c.tag, b.tag];
        [b.stunde, c.stunde] = [c.stunde, b.stunde];
        place(bi, 1);
        place(bj, 1);
      }
    }
    if (cost < bestCost) {
      bestCost = cost;
      bestState = movable.map((x) => [blocks[x].tag, blocks[x].stunde] as const);
    }
    if (o.onProgress && it % reportEvery === 0) o.onProgress(it / iters, bestCost);
  }

  // Beste Lösung wiederherstellen
  for (const bi of movable) place(bi, -1);
  movable.forEach((bi, i) => {
    blocks[bi].tag = bestState[i][0];
    blocks[bi].stunde = bestState[i][1];
    place(bi, 1);
  });
  const finalCost = totalCost();
  const harte = hardCount();

  // ---- Planeinträge erzeugen und Räume zuweisen
  const result: Planeintrag[] = [];
  const roomBusy = new Set<string>();
  for (const b of blocks) {
    if (b.locked && b.fixedEntry) {
      result.push(b.fixedEntry);
      if (b.fixedEntry.raumId) roomBusy.add(`${b.fixedEntry.raumId}@${b.tag}:${b.stunde}`);
    }
  }
  let ohneRaum = 0;
  const pending: { u: Unterricht; tag: number; stunde: number; len: number }[] = [];
  for (const b of blocks) if (!b.locked) for (const u of b.members) pending.push({ u, tag: b.tag, stunde: b.stunde, len: b.len });
  // Unterricht mit wenigen Raumoptionen zuerst
  const cands = (u: Unterricht): string[] => {
    const r = raeumeFuer(u);
    if (r.length) return r;
    if (u.klassenIds.length === 1) {
      const k = idx.klassen.get(u.klassenIds[0]);
      if (k?.raumId) return [k.raumId];
    }
    return [];
  };
  pending.sort((a, b) => (cands(a.u).length || 99) - (cands(b.u).length || 99));
  for (const pe of pending) {
    const list = cands(pe.u);
    let chosen: string | null = null;
    for (const rid of list) {
      const raum = idx.raeume.get(rid);
      let ok = true;
      for (let k = 0; k < pe.len; k++) {
        if (roomBusy.has(`${rid}@${pe.tag}:${pe.stunde + k}`) || (raum && hasSlot(raum.sperrzeiten, pe.tag, pe.stunde + k))) ok = false;
      }
      if (ok) {
        chosen = rid;
        break;
      }
    }
    if (!chosen && list.length) {
      ohneRaum++;
    }
    for (let k = 0; k < pe.len; k++) {
      if (chosen) roomBusy.add(`${chosen}@${pe.tag}:${pe.stunde + k}`);
      result.push({ id: newId('pl'), unterrichtId: pe.u.id, tag: pe.tag, stunde: pe.stunde + k, raumId: chosen, fixiert: false });
    }
  }

  return { stundenplan: result, harteKonflikte: harte, kosten: finalCost, bloecke: movable.length, ohneRaum, dauerMs: Date.now() - t0 };
}
