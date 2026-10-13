// Import/Export: CSV (Schild-NRW-Exporte, Tabellen), Untis-GPU-Austauschformat, iCalendar.

import { addDays, daysBetween, weekdayIndex } from './dates';
import { newFach, newKlasse, newLehrer, newRaum, newUnterricht } from './factory';
import { buildIndex, kurzKlassen, kurzLehrer } from './index';
import { newId, type SchoolData } from './model';
import { istUnterrichtstag, tagesplan, vertretungsplanZeilen } from './vertretung';

// ---------------------------------------------------------------------------
// CSV
// ---------------------------------------------------------------------------

export function parseCSV(text: string, delimiter?: string): string[][] {
  const clean = text.replace(/^﻿/, '');
  const firstLine = clean.split(/\r?\n/, 1)[0] ?? '';
  const delim =
    delimiter ??
    ([';', ',', '\t'] as const)
      .map((c) => [c, firstLine.split(c).length] as const)
      .sort((a, b) => b[1] - a[1])[0][0];
  const rows: string[][] = [];
  let row: string[] = [];
  let field = '';
  let q = false;
  for (let i = 0; i < clean.length; i++) {
    const ch = clean[i];
    if (q) {
      if (ch === '"') {
        if (clean[i + 1] === '"') {
          field += '"';
          i++;
        } else q = false;
      } else field += ch;
    } else if (ch === '"') q = true;
    else if (ch === delim) {
      row.push(field);
      field = '';
    } else if (ch === '\n' || ch === '\r') {
      if (ch === '\r' && clean[i + 1] === '\n') i++;
      row.push(field);
      if (row.some((c) => c.trim() !== '')) rows.push(row);
      row = [];
      field = '';
    } else field += ch;
  }
  row.push(field);
  if (row.some((c) => c.trim() !== '')) rows.push(row);
  return rows.map((r) => r.map((c) => c.trim()));
}

export function toCSV(rows: (string | number)[][], delim = ';'): string {
  const esc = (v: string | number) => {
    const s = String(v ?? '');
    return /[";\n\r,]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
  };
  return '﻿' + rows.map((r) => r.map(esc).join(delim)).join('\r\n') + '\r\n';
}

const norm = (s: string) => s.toLowerCase().replace(/[^a-zäöüß0-9]/g, '');

function findCol(header: string[], ...names: string[]): number {
  const h = header.map(norm);
  for (const n of names) {
    const i = h.indexOf(norm(n));
    if (i >= 0) return i;
  }
  for (const n of names) {
    const i = h.findIndex((x) => x.includes(norm(n)));
    if (i >= 0) return i;
  }
  return -1;
}

export interface ImportBericht {
  neu: number;
  aktualisiert: number;
  fehler: string[];
}

/** Lehrkräfte aus CSV (z. B. Schild-NRW-Export "Lehrkräfte") importieren */
export function importLehrerCSV(d: SchoolData, text: string): ImportBericht {
  const rows = parseCSV(text);
  const bericht: ImportBericht = { neu: 0, aktualisiert: 0, fehler: [] };
  if (rows.length < 2) return { ...bericht, fehler: ['Datei enthält keine Datenzeilen'] };
  const [h, ...data] = rows;
  const cK = findCol(h, 'Kürzel', 'Kuerzel', 'Lehrerkürzel', 'Kurzzeichen');
  const cN = findCol(h, 'Nachname', 'Name');
  const cV = findCol(h, 'Vorname');
  const cE = findCol(h, 'E-Mail', 'Email', 'Mail');
  const cP = findCol(h, 'Pflichtstunden', 'Pflichtstd', 'Soll', 'Deputat');
  const cF = findCol(h, 'Fächer', 'Faecher', 'Lehrbefähigung');
  if (cK < 0) return { ...bericht, fehler: ['Spalte "Kürzel" nicht gefunden'] };
  for (const r of data) {
    const kuerzel = r[cK];
    if (!kuerzel) continue;
    let l = d.lehrer.find((x) => x.kuerzel.toLowerCase() === kuerzel.toLowerCase());
    if (!l) {
      l = newLehrer({ kuerzel });
      d.lehrer.push(l);
      bericht.neu++;
    } else bericht.aktualisiert++;
    if (cN >= 0 && r[cN]) l.nachname = r[cN];
    if (cV >= 0 && r[cV]) l.vorname = r[cV];
    if (cE >= 0 && r[cE]) l.email = r[cE];
    if (cP >= 0 && r[cP]) {
      const n = Number(r[cP].replace(',', '.'));
      if (!Number.isNaN(n)) l.pflichtstunden = n;
    }
    if (cF >= 0 && r[cF]) {
      const ids = r[cF]
        .split(/[ ,/|]+/)
        .map((k) => d.faecher.find((f) => f.kuerzel.toLowerCase() === k.toLowerCase())?.id)
        .filter((x): x is string => !!x);
      if (ids.length) l.faecher = ids;
    }
  }
  return bericht;
}

/** Klassen aus CSV (z. B. Schild-NRW "Klassen") importieren */
export function importKlassenCSV(d: SchoolData, text: string): ImportBericht {
  const rows = parseCSV(text);
  const bericht: ImportBericht = { neu: 0, aktualisiert: 0, fehler: [] };
  if (rows.length < 2) return { ...bericht, fehler: ['Datei enthält keine Datenzeilen'] };
  const [h, ...data] = rows;
  const cN = findCol(h, 'Klasse', 'Klassenbezeichnung', 'Name');
  const cJ = findCol(h, 'Jahrgang', 'Jahrgangsstufe', 'Stufe');
  const cL = findCol(h, 'Klassenlehrer', 'Klassenleitung', 'KL');
  const cS = findCol(h, 'Schüler', 'Schueler', 'Anzahl');
  if (cN < 0) return { ...bericht, fehler: ['Spalte "Klasse" nicht gefunden'] };
  for (const r of data) {
    const name = r[cN];
    if (!name) continue;
    let k = d.klassen.find((x) => x.name.toLowerCase() === name.toLowerCase());
    if (!k) {
      k = newKlasse({ name, jahrgang: name.match(/^\d+/)?.[0] ?? name });
      d.klassen.push(k);
      bericht.neu++;
    } else bericht.aktualisiert++;
    if (cJ >= 0 && r[cJ]) k.jahrgang = r[cJ].replace(/^0/, '');
    if (cS >= 0 && r[cS]) k.schuelerzahl = Number(r[cS]) || k.schuelerzahl;
    if (cL >= 0 && r[cL]) {
      const ids = r[cL]
        .split(/[ ,/]+/)
        .map((kz) => d.lehrer.find((l) => l.kuerzel.toLowerCase() === kz.toLowerCase())?.id)
        .filter((x): x is string => !!x);
      if (ids.length) k.klassenlehrerIds = ids;
    }
  }
  return bericht;
}

// ---------------------------------------------------------------------------
// GPU-Austauschformat (Untis-Export, für die Migration bestehender Daten)
// GPU001 Stundenplan, GPU002 Unterricht, GPU003 Klassen, GPU004 Lehrer, GPU005 Räume, GPU006 Fächer
// ---------------------------------------------------------------------------

export interface GpuDatei {
  name: string;
  inhalt: string;
}

export function importGPU(d: SchoolData, dateien: GpuDatei[]): ImportBericht & { details: string[] } {
  const bericht = { neu: 0, aktualisiert: 0, fehler: [] as string[], details: [] as string[] };
  const get = (nr: string) => dateien.find((f) => f.name.toUpperCase().includes(`GPU${nr}`));
  const rows = (nr: string) => {
    const f = get(nr);
    return f ? parseCSV(f.inhalt, f.inhalt.includes(';') ? ';' : ',') : [];
  };

  const byK = <T extends { kuerzel: string }>(arr: T[], k: string) => arr.find((x) => x.kuerzel.toLowerCase() === k.toLowerCase());

  for (const r of rows('006')) {
    if (!r[0]) continue;
    const fa = byK(d.faecher, r[0]);
    if (fa) {
      if (r[1]) fa.name = r[1];
      bericht.aktualisiert++;
    } else {
      d.faecher.push(newFach({ kuerzel: r[0], name: r[1] || r[0] }));
      bericht.neu++;
    }
  }
  bericht.details.push(`Fächer: ${rows('006').length}`);
  for (const r of rows('005')) {
    if (!r[0]) continue;
    const ra = byK(d.raeume, r[0]);
    const kap = Number(r[3]) || 30;
    if (ra) {
      if (r[1]) ra.name = r[1];
      bericht.aktualisiert++;
    } else {
      d.raeume.push(newRaum({ kuerzel: r[0], name: r[1] || r[0], kapazitaet: kap }));
      bericht.neu++;
    }
  }
  bericht.details.push(`Räume: ${rows('005').length}`);
  for (const r of rows('004')) {
    if (!r[0]) continue;
    const l = byK(d.lehrer, r[0]);
    if (l) {
      if (r[1]) l.nachname = r[1];
      bericht.aktualisiert++;
    } else {
      d.lehrer.push(newLehrer({ kuerzel: r[0], nachname: r[1] || r[0] }));
      bericht.neu++;
    }
  }
  bericht.details.push(`Lehrkräfte: ${rows('004').length}`);
  for (const r of rows('003')) {
    if (!r[0]) continue;
    const k = d.klassen.find((x) => x.name.toLowerCase() === r[0].toLowerCase());
    if (k) bericht.aktualisiert++;
    else {
      d.klassen.push(newKlasse({ name: r[0], jahrgang: r[0].match(/^\d+/)?.[0] ?? r[0] }));
      bericht.neu++;
    }
  }
  bericht.details.push(`Klassen: ${rows('003').length}`);

  // Unterricht: mehrere Zeilen mit gleicher Nummer bilden eine Kopplung
  const uMap = new Map<string, ReturnType<typeof newUnterricht>>();
  for (const r of rows('002')) {
    const nr = r[0];
    if (!nr) continue;
    let u = uMap.get(nr);
    if (!u) {
      u = newUnterricht({ wochenstunden: Number((r[1] || '0').replace(',', '.')) || 0, notiz: `GPU-Nr. ${nr}` });
      uMap.set(nr, u);
    }
    const k = d.klassen.find((x) => x.name.toLowerCase() === (r[4] || '').toLowerCase());
    const l = r[5] ? byK(d.lehrer, r[5]) : undefined;
    const fa = r[6] ? byK(d.faecher, r[6]) : undefined;
    const ra = r[7] ? byK(d.raeume, r[7]) : undefined;
    if (k && !u.klassenIds.includes(k.id)) u.klassenIds.push(k.id);
    if (l && !u.lehrerIds.includes(l.id)) u.lehrerIds.push(l.id);
    if (fa && !u.fachId) u.fachId = fa.id;
    if (ra && !u.raumIds.includes(ra.id)) u.raumIds.push(ra.id);
  }
  const gpuNr = new Map<string, string>();
  for (const [nr, u] of uMap) {
    if (!u.fachId) {
      bericht.fehler.push(`Unterricht ${nr}: Fach unbekannt – übersprungen`);
      continue;
    }
    d.unterricht.push(u);
    gpuNr.set(nr, u.id);
    bericht.neu++;
  }
  bericht.details.push(`Unterrichte: ${gpuNr.size}`);

  // Stundenplan
  let plan = 0;
  const seen = new Set<string>();
  for (const r of rows('001')) {
    const uid = gpuNr.get(r[0]);
    if (!uid) continue;
    const tag = Number(r[5]) - 1;
    const stunde = Number(r[6]);
    if (Number.isNaN(tag) || Number.isNaN(stunde)) continue;
    const key = `${uid}:${tag}:${stunde}`;
    if (seen.has(key)) continue; // gekoppelte Zeilen nur einmal
    seen.add(key);
    const ra = r[4] ? byK(d.raeume, r[4]) : undefined;
    d.stundenplan.push({ id: newId('pl'), unterrichtId: uid, tag, stunde, raumId: ra?.id ?? null, fixiert: false });
    plan++;
  }
  bericht.details.push(`Planeinträge: ${plan}`);
  return bericht;
}

/** Export im GPU-Format (GPU001 Stundenplan / GPU002 Unterricht) für den Austausch mit Fremdsystemen */
export function exportGPU(d: SchoolData): GpuDatei[] {
  const idx = buildIndex(d);
  const nr = new Map(d.unterricht.map((u, i) => [u.id, i + 1] as const));
  const g002: (string | number)[][] = [];
  for (const u of d.unterricht) {
    const fach = idx.faecher.get(u.fachId)?.kuerzel ?? '';
    const raum = u.raumIds[0] ? idx.raeume.get(u.raumIds[0])?.kuerzel ?? '' : '';
    const klassen = u.klassenIds.length ? u.klassenIds : [''];
    const lehrer = u.lehrerIds.length ? u.lehrerIds : [''];
    const n = Math.max(klassen.length, lehrer.length);
    for (let i = 0; i < n; i++) {
      g002.push([
        nr.get(u.id)!,
        u.wochenstunden,
        '',
        '',
        klassen[i] ? idx.klassen.get(klassen[i])?.name ?? '' : '',
        lehrer[i] ? idx.lehrer.get(lehrer[i])?.kuerzel ?? '' : '',
        fach,
        raum,
      ]);
    }
  }
  const g001: (string | number)[][] = [];
  for (const p of d.stundenplan) {
    const u = idx.unterricht.get(p.unterrichtId);
    if (!u) continue;
    const raum = p.raumId ? idx.raeume.get(p.raumId)?.kuerzel ?? '' : '';
    for (const k of u.klassenIds.length ? u.klassenIds : ['']) {
      g001.push([nr.get(u.id)!, k ? idx.klassen.get(k)?.name ?? '' : '', kurzLehrer(idx, u.lehrerIds.slice(0, 1)), idx.faecher.get(u.fachId)?.kuerzel ?? '', raum, p.tag + 1, p.stunde, '']);
    }
  }
  const strip = (s: string) => s.replace(/^﻿/, '');
  return [
    { name: 'GPU001.TXT', inhalt: strip(toCSV(g001)) },
    { name: 'GPU002.TXT', inhalt: strip(toCSV(g002)) },
    { name: 'GPU003.TXT', inhalt: strip(toCSV(d.klassen.map((k) => [k.name, k.name]))) },
    { name: 'GPU004.TXT', inhalt: strip(toCSV(d.lehrer.map((l) => [l.kuerzel, l.nachname]))) },
    { name: 'GPU005.TXT', inhalt: strip(toCSV(d.raeume.map((r) => [r.kuerzel, r.name, '', r.kapazitaet]))) },
    { name: 'GPU006.TXT', inhalt: strip(toCSV(d.faecher.map((f) => [f.kuerzel, f.name]))) },
  ];
}

// ---------------------------------------------------------------------------
// Tabellen-Exporte
// ---------------------------------------------------------------------------

export function exportUnterrichtsverteilung(d: SchoolData): string {
  const idx = buildIndex(d);
  const rows: (string | number)[][] = [['Fach', 'Klassen', 'Lehrkräfte', 'Wochenstunden', 'Doppelstunden', 'Gruppe/Kurs', 'Kursart', 'Schiene', 'Halbjahr']];
  for (const u of d.unterricht)
    rows.push([
      idx.faecher.get(u.fachId)?.kuerzel ?? '',
      kurzKlassen(idx, u.klassenIds),
      kurzLehrer(idx, u.lehrerIds),
      String(u.wochenstunden).replace('.', ','),
      u.doppelstunden,
      u.kursname || u.gruppe,
      u.kursart ?? '',
      u.schiene,
      u.halbjahr === 0 ? 'ganzjährig' : `${u.halbjahr}. HJ`,
    ]);
  return toCSV(rows);
}

export function exportVertretungsplanCSV(d: SchoolData, iso: string): string {
  const rows: (string | number)[][] = [['Datum', 'Stunde', 'Klasse(n)', 'Fach', 'Lehrkraft', 'Vertretung', 'Raum', 'Art', 'Text']];
  for (const z of vertretungsplanZeilen(d, iso)) rows.push([iso.split('-').reverse().join('.'), z.stunde, z.klassen, z.fachNeu, z.lehrer, z.vertreter, z.raumNeu, z.art, z.text]);
  return toCSV(rows);
}

// ---------------------------------------------------------------------------
// iCalendar (persönlicher Stundenplan inkl. Vertretungen)
// ---------------------------------------------------------------------------

export function exportICal(d: SchoolData, lehrerId: string, von: string, bis: string): string {
  const idx = buildIndex(d);
  const l = idx.lehrer.get(lehrerId);
  const lines = ['BEGIN:VCALENDAR', 'VERSION:2.0', 'PRODID:-//UntPlan//Stundenplan//DE', 'CALSCALE:GREGORIAN', `X-WR-CALNAME:Stundenplan ${l?.kuerzel ?? ''}`];
  const stamp = new Date().toISOString().replace(/[-:]/g, '').replace(/\.\d+Z$/, 'Z');
  for (const iso of daysBetween(von, bis)) {
    if (!istUnterrichtstag(d, iso)) continue;
    const zeilen = vertretungsplanZeilen(d, iso);
    for (const p of tagesplan(d, iso, idx)) {
      const u = idx.unterricht.get(p.unterrichtId)!;
      if (!u.lehrerIds.includes(lehrerId)) continue;
      const v = d.vertretungen.find((x) => x.datum === iso && x.planeintragId === p.id);
      if (v && v.art !== 'Raumänderung' && !v.vertreterIds.includes(lehrerId)) continue;
      lines.push(...event(iso, p.stunde, `${idx.faecher.get(u.fachId)?.kuerzel ?? ''} ${kurzKlassen(idx, u.klassenIds)}`, (v?.raumId ? idx.raeume.get(v.raumId)?.kuerzel : p.raumId ? idx.raeume.get(p.raumId)?.kuerzel : '') ?? ''));
    }
    for (const z of zeilen) {
      if (!z.vertreterIds.includes(lehrerId) || z.lehrerIds.includes(lehrerId)) continue;
      lines.push(...event(iso, z.stunde, `Vertretung ${z.fachNeu} ${z.klassen}`, z.raumNeu));
    }
  }
  lines.push('END:VCALENDAR');
  return lines.join('\r\n') + '\r\n';

  function event(iso: string, stunde: number, summary: string, location: string): string[] {
    const per = d.zeitraster.stunden.find((s) => s.nr === stunde);
    if (!per) return [];
    const dt = (t: string) => `${iso.replace(/-/g, '')}T${t.replace(':', '')}00`;
    return [
      'BEGIN:VEVENT',
      `UID:${iso}-${stunde}-${lehrerId}-${summary.replace(/\W/g, '')}@untplan`,
      `DTSTAMP:${stamp}`,
      `DTSTART;TZID=Europe/Berlin:${dt(per.beginn)}`,
      `DTEND;TZID=Europe/Berlin:${dt(per.ende)}`,
      `SUMMARY:${summary}`,
      location ? `LOCATION:${location}` : '',
      'END:VEVENT',
    ].filter(Boolean);
  }
}

/** Liefert die nächsten n Unterrichtstage ab einem Datum */
export function naechsteUnterrichtstage(d: SchoolData, ab: string, n: number): string[] {
  const out: string[] = [];
  let x = ab;
  for (let i = 0; out.length < n && i < 120; i++, x = addDays(x, 1)) if (istUnterrichtstag(d, x) && weekdayIndex(x) < d.zeitraster.tage) out.push(x);
  return out;
}
