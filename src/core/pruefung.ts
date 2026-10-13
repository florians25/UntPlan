// Plausibilitäts- und Konfliktprüfung des Stundenplans ("Diagnose").

import { buildIndex, kurzKlassen, type DataIndex } from './index';
import { TAGE_KURZ, hasSlot, sollStunden, type Planeintrag, type SchoolData, type Unterricht } from './model';
import { istStundenLehrer } from './index';

export type Schwere = 'fehler' | 'warnung' | 'hinweis';

export interface Befund {
  schwere: Schwere;
  kategorie: string;
  text: string;
  planeintragIds: string[];
  /** Für Navigation in der Oberfläche */
  ref?: { typ: 'lehrer' | 'klasse' | 'raum' | 'unterricht'; id: string };
}

interface SlotEntry {
  p: Planeintrag;
  u: Unterricht;
}

const ort = (p: { tag: number; stunde: number }) => `${TAGE_KURZ[p.tag]} ${p.stunde}.`;

/** Dürfen zwei Unterrichte derselben Klasse parallel liegen? (Schiene oder verschiedene Teilgruppen) */
export function parallelErlaubt(a: Unterricht, b: Unterricht): boolean {
  if (a.id === b.id) return true;
  if (a.schiene && a.schiene === b.schiene) return true;
  if (a.gruppe && b.gruppe && a.gruppe !== b.gruppe) return true;
  return false;
}

export function pruefeStundenplan(d: SchoolData, idx: DataIndex = buildIndex(d)): Befund[] {
  const out: Befund[] = [];
  const maxStunde = d.zeitraster.stunden.length;
  const bySlot = new Map<string, SlotEntry[]>();

  for (const p of d.stundenplan) {
    const u = idx.unterricht.get(p.unterrichtId);
    if (!u) {
      out.push({ schwere: 'fehler', kategorie: 'Daten', text: `Planeintrag ${ort(p)} verweist auf gelöschten Unterricht`, planeintragIds: [p.id] });
      continue;
    }
    if (p.tag >= d.zeitraster.tage || p.stunde < 1 || p.stunde > maxStunde) {
      out.push({ schwere: 'fehler', kategorie: 'Zeitraster', text: `Unterricht liegt außerhalb des Zeitrasters (${ort(p)})`, planeintragIds: [p.id] });
    }
    const key = `${p.tag}:${p.stunde}`;
    const list = bySlot.get(key);
    if (list) list.push({ p, u });
    else bySlot.set(key, [{ p, u }]);
  }

  for (const entries of bySlot.values()) {
    const { tag, stunde } = entries[0].p;
    // Lehrkräfte
    const lehrerMap = new Map<string, SlotEntry[]>();
    const klassenMap = new Map<string, SlotEntry[]>();
    const raumMap = new Map<string, SlotEntry[]>();
    for (const e of entries) {
      for (const l of e.u.lehrerIds) push(lehrerMap, l, e);
      for (const k of e.u.klassenIds) push(klassenMap, k, e);
      if (e.p.raumId) push(raumMap, e.p.raumId, e);
    }
    for (const [lid, es] of lehrerMap) {
      const distinct = uniqueBy(es, (e) => e.u.id);
      if (distinct.length > 1) {
        const l = idx.lehrer.get(lid);
        out.push({
          schwere: 'fehler',
          kategorie: 'Lehrer-Kollision',
          text: `${l?.kuerzel ?? '?'} ist ${ort({ tag, stunde })} ${distinct.length}× verplant`,
          planeintragIds: distinct.map((e) => e.p.id),
          ref: { typ: 'lehrer', id: lid },
        });
      }
    }
    for (const [kid, es] of klassenMap) {
      const distinct = uniqueBy(es, (e) => e.u.id);
      let konflikt = false;
      for (let i = 0; i < distinct.length && !konflikt; i++)
        for (let j = i + 1; j < distinct.length; j++)
          if (!parallelErlaubt(distinct[i].u, distinct[j].u)) {
            konflikt = true;
            break;
          }
      if (konflikt) {
        out.push({
          schwere: 'fehler',
          kategorie: 'Klassen-Kollision',
          text: `Klasse ${idx.klassen.get(kid)?.name ?? '?'} hat ${ort({ tag, stunde })} ${distinct.length} Unterrichte gleichzeitig`,
          planeintragIds: distinct.map((e) => e.p.id),
          ref: { typ: 'klasse', id: kid },
        });
      }
    }
    for (const [rid, es] of raumMap) {
      const distinct = uniqueBy(es, (e) => e.u.id);
      if (distinct.length > 1) {
        out.push({
          schwere: 'fehler',
          kategorie: 'Raum-Kollision',
          text: `Raum ${idx.raeume.get(rid)?.kuerzel ?? '?'} ist ${ort({ tag, stunde })} doppelt belegt`,
          planeintragIds: distinct.map((e) => e.p.id),
          ref: { typ: 'raum', id: rid },
        });
      }
    }
    // Sperrzeiten und Raumtyp
    for (const e of entries) {
      for (const lid of e.u.lehrerIds) {
        const l = idx.lehrer.get(lid);
        if (l && hasSlot(l.sperrzeiten, tag, stunde))
          out.push({ schwere: 'fehler', kategorie: 'Sperrzeit', text: `${l.kuerzel} ist ${ort({ tag, stunde })} gesperrt`, planeintragIds: [e.p.id], ref: { typ: 'lehrer', id: lid } });
      }
      for (const kid of e.u.klassenIds) {
        const k = idx.klassen.get(kid);
        if (k && hasSlot(k.sperrzeiten, tag, stunde))
          out.push({ schwere: 'fehler', kategorie: 'Sperrzeit', text: `Klasse ${k.name} ist ${ort({ tag, stunde })} gesperrt`, planeintragIds: [e.p.id], ref: { typ: 'klasse', id: kid } });
      }
      if (e.p.raumId) {
        const r = idx.raeume.get(e.p.raumId);
        if (r && hasSlot(r.sperrzeiten, tag, stunde))
          out.push({ schwere: 'fehler', kategorie: 'Sperrzeit', text: `Raum ${r.kuerzel} ist ${ort({ tag, stunde })} gesperrt`, planeintragIds: [e.p.id], ref: { typ: 'raum', id: r.id } });
        const fach = idx.faecher.get(e.u.fachId);
        if (r && fach?.raumTyp && r.typ !== fach.raumTyp && !e.u.raumIds.includes(r.id))
          out.push({ schwere: 'warnung', kategorie: 'Raumtyp', text: `${fach.kuerzel} ${kurzKlassen(idx, e.u.klassenIds)} ${ort({ tag, stunde })}: ${fach.raumTyp} benötigt, ${r.kuerzel} ist ${r.typ}`, planeintragIds: [e.p.id] });
      }
    }
  }

  // Verplante Stunden je Unterricht
  for (const u of d.unterricht) {
    const n = idx.plaeneProUnterricht.get(u.id)?.length ?? 0;
    const fach = idx.faecher.get(u.fachId);
    if (n < u.wochenstunden)
      out.push({ schwere: 'warnung', kategorie: 'Nicht verplant', text: `${fach?.kuerzel ?? '?'} ${kurzKlassen(idx, u.klassenIds)}: ${u.wochenstunden - n} von ${u.wochenstunden} Std. nicht verplant`, planeintragIds: [], ref: { typ: 'unterricht', id: u.id } });
    else if (n > u.wochenstunden)
      out.push({ schwere: 'warnung', kategorie: 'Überverplant', text: `${fach?.kuerzel ?? '?'} ${kurzKlassen(idx, u.klassenIds)}: ${n - u.wochenstunden} Std. zu viel verplant`, planeintragIds: [], ref: { typ: 'unterricht', id: u.id } });
    if (u.lehrerIds.length === 0)
      out.push({ schwere: 'hinweis', kategorie: 'Unterrichtsverteilung', text: `${fach?.kuerzel ?? '?'} ${kurzKlassen(idx, u.klassenIds)} hat keine Lehrkraft`, planeintragIds: [], ref: { typ: 'unterricht', id: u.id } });
    for (const lid of u.lehrerIds) {
      const l = idx.lehrer.get(lid);
      if (l && fach && l.faecher.length > 0 && !l.faecher.includes(fach.id))
        out.push({ schwere: 'hinweis', kategorie: 'Fachfremd', text: `${l.kuerzel} unterrichtet ${fach.kuerzel} fachfremd (${kurzKlassen(idx, u.klassenIds)})`, planeintragIds: [], ref: { typ: 'lehrer', id: lid } });
    }
  }

  // Tagesbelastung, Hohlstunden
  for (const l of d.lehrer) {
    for (let t = 0; t < d.zeitraster.tage; t++) {
      const stunden = d.stundenplan.filter((p) => p.tag === t && idx.unterricht.get(p.unterrichtId)?.lehrerIds.includes(l.id)).map((p) => p.stunde);
      const uniq = [...new Set(stunden)];
      if (uniq.length > l.maxStundenProTag)
        out.push({ schwere: 'warnung', kategorie: 'Tagesbelastung', text: `${l.kuerzel} hat ${TAGE_KURZ[t]} ${uniq.length} Stunden (max. ${l.maxStundenProTag})`, planeintragIds: [], ref: { typ: 'lehrer', id: l.id } });
    }
    const ist = istStundenLehrer(d, l.id);
    const soll = sollStunden(l);
    if (l.aktiv && Math.abs(ist - soll) >= 1)
      out.push({ schwere: 'hinweis', kategorie: 'Deputat', text: `${l.kuerzel}: Ist ${ist} / Soll ${soll} Std. (${ist - soll > 0 ? '+' : ''}${ist - soll})`, planeintragIds: [], ref: { typ: 'lehrer', id: l.id } });
  }
  for (const k of d.klassen) {
    if (!k.keineHohlstunden) continue;
    for (let t = 0; t < d.zeitraster.tage; t++) {
      const stunden = new Set(d.stundenplan.filter((p) => p.tag === t && idx.unterricht.get(p.unterrichtId)?.klassenIds.includes(k.id)).map((p) => p.stunde));
      if (stunden.size === 0) continue;
      const min = Math.min(...stunden);
      const max = Math.max(...stunden);
      const luecken: number[] = [];
      for (let s = min; s <= max; s++) if (!stunden.has(s) && s !== d.zeitraster.mittagspauseStunde) luecken.push(s);
      if (luecken.length)
        out.push({ schwere: 'warnung', kategorie: 'Hohlstunde', text: `Klasse ${k.name} hat ${TAGE_KURZ[t]} Hohlstunde(n) in ${luecken.join(', ')}.`, planeintragIds: [], ref: { typ: 'klasse', id: k.id } });
    }
  }
  const order: Record<Schwere, number> = { fehler: 0, warnung: 1, hinweis: 2 };
  return out.sort((a, b) => order[a.schwere] - order[b.schwere]);
}

function push<K, V>(m: Map<K, V[]>, k: K, v: V) {
  const l = m.get(k);
  if (l) l.push(v);
  else m.set(k, [v]);
}

function uniqueBy<T>(arr: T[], key: (t: T) => string): T[] {
  const seen = new Set<string>();
  return arr.filter((x) => {
    const k = key(x);
    if (seen.has(k)) return false;
    seen.add(k);
    return true;
  });
}

/** Kann ein Unterricht an Tag/Stunde gelegt werden, ohne harte Konflikte zu erzeugen? */
export function slotFrei(d: SchoolData, idx: DataIndex, u: Unterricht, tag: number, stunde: number, ignorePlanIds: string[] = []): string[] {
  const gruende: string[] = [];
  for (const lid of u.lehrerIds) {
    const l = idx.lehrer.get(lid);
    if (l && hasSlot(l.sperrzeiten, tag, stunde)) gruende.push(`${l.kuerzel} gesperrt`);
  }
  for (const kid of u.klassenIds) {
    const k = idx.klassen.get(kid);
    if (k && hasSlot(k.sperrzeiten, tag, stunde)) gruende.push(`${k.name} gesperrt`);
  }
  for (const p of d.stundenplan) {
    if (p.tag !== tag || p.stunde !== stunde || ignorePlanIds.includes(p.id) || p.unterrichtId === u.id) continue;
    const o = idx.unterricht.get(p.unterrichtId);
    if (!o) continue;
    for (const lid of u.lehrerIds) if (o.lehrerIds.includes(lid)) gruende.push(`${idx.lehrer.get(lid)?.kuerzel} belegt`);
    if (!parallelErlaubt(u, o)) for (const kid of u.klassenIds) if (o.klassenIds.includes(kid)) gruende.push(`${idx.klassen.get(kid)?.name} belegt`);
  }
  return [...new Set(gruende)];
}

/** Schlägt einen freien, passenden Raum für einen Unterricht zu Tag/Stunde vor */
export function standardRaum(d: SchoolData, idx: DataIndex, u: Unterricht, tag: number, stunde: number, ignorePlanIds: string[] = []): string | null {
  const belegt = new Set(d.stundenplan.filter((p) => p.tag === tag && p.stunde === stunde && !ignorePlanIds.includes(p.id) && p.raumId).map((p) => p.raumId!));
  const frei = (id: string) => {
    const r = idx.raeume.get(id);
    return !!r && !belegt.has(id) && !hasSlot(r.sperrzeiten, tag, stunde);
  };
  if (u.raumIds.length) return u.raumIds.find(frei) ?? u.raumIds[0];
  const fach = idx.faecher.get(u.fachId);
  if (fach?.raumTyp) {
    const r = d.raeume.find((x) => x.typ === fach.raumTyp && frei(x.id));
    if (r) return r.id;
  }
  if (u.klassenIds.length === 1) {
    const k = idx.klassen.get(u.klassenIds[0]);
    if (k?.raumId) return k.raumId;
  }
  return d.raeume.find((r) => r.typ === 'Klassenraum' && frei(r.id))?.id ?? null;
}
