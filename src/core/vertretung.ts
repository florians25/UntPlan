// Vertretungsplanung: Ermittlung betroffener Stunden, Vertretungsvorschläge, Tagesplan.

import { addDays, fromISO, inRange, mondayOf, weekdayIndex } from './dates';
import { buildIndex, kurzKlassen, kurzLehrer, type DataIndex } from './index';
import { jahrgangRang } from './nrw';
import {
  hasSlot,
  newId,
  type Abwesenheit,
  type FreierTag,
  type Planeintrag,
  type SchoolData,
  type Unterricht,
  type Vertretung,
  type VertretungsArt,
} from './model';

export function freierTagAm(d: SchoolData, iso: string): FreierTag | undefined {
  return d.freieTage.find((f) => inRange(iso, f.von, f.bis));
}

export function istUnterrichtstag(d: SchoolData, iso: string): boolean {
  const wd = weekdayIndex(iso);
  return wd < d.zeitraster.tage && !freierTagAm(d, iso);
}

export function halbjahrAm(d: SchoolData, iso: string): 1 | 2 {
  return d.meta.halbjahrWechsel && iso >= d.meta.halbjahrWechsel ? 2 : 1;
}

/** Regulärer Stundenplan eines Datums (ohne Vertretungen) */
export function tagesplan(d: SchoolData, iso: string, idx: DataIndex = buildIndex(d)): Planeintrag[] {
  if (!istUnterrichtstag(d, iso)) return [];
  const wd = weekdayIndex(iso);
  const hj = halbjahrAm(d, iso);
  return d.stundenplan.filter((p) => {
    if (p.tag !== wd) return false;
    const u = idx.unterricht.get(p.unterrichtId);
    return !!u && (u.halbjahr === 0 || u.halbjahr === hj);
  });
}

export function abwesenheitenAm(d: SchoolData, iso: string): Abwesenheit[] {
  return d.abwesenheiten.filter((a) => inRange(iso, a.von, a.bis));
}

function deckt(a: Abwesenheit, iso: string, stunde: number): boolean {
  if (!inRange(iso, a.von, a.bis)) return false;
  // Stundenangaben gelten am ersten und letzten Tag, dazwischen ganztägig
  const von = iso === a.von ? a.stundeVon : 1;
  const bis = iso === a.bis ? a.stundeBis : 99;
  if (a.von === a.bis) return stunde >= a.stundeVon && stunde <= a.stundeBis;
  return stunde >= von && stunde <= bis;
}

export function lehrerAbwesend(d: SchoolData, lehrerId: string, iso: string, stunde: number): Abwesenheit | undefined {
  return d.abwesenheiten.find(
    (a) => deckt(a, iso, stunde) && ((a.typ === 'lehrer' && a.refId === lehrerId) || (a.typ === 'klasse' && a.begleitLehrerIds.includes(lehrerId))),
  );
}

export function klasseAbwesend(d: SchoolData, klasseId: string, iso: string, stunde: number): Abwesenheit | undefined {
  return d.abwesenheiten.find((a) => a.typ === 'klasse' && a.refId === klasseId && deckt(a, iso, stunde));
}

export function raumGesperrt(d: SchoolData, raumId: string, iso: string, stunde: number): Abwesenheit | undefined {
  return d.abwesenheiten.find((a) => a.typ === 'raum' && a.refId === raumId && deckt(a, iso, stunde));
}

export type Betroffenheit = 'lehrer' | 'klasse' | 'raum';

export interface BetroffeneStunde {
  planeintrag: Planeintrag;
  unterricht: Unterricht;
  stunde: number;
  grund: Betroffenheit;
  abwesendeLehrerIds: string[];
  vertretung: Vertretung | undefined;
  /** Vorschlag, ob Entfall zulässig/sinnvoll ist */
  entfallMoeglich: boolean;
}

export function vertretungenAm(d: SchoolData, iso: string): Vertretung[] {
  return d.vertretungen.filter((v) => v.datum === iso);
}

/** Alle Stunden eines Tages, die wegen Abwesenheiten bearbeitet werden müssen */
export function betroffeneStunden(d: SchoolData, iso: string, idx: DataIndex = buildIndex(d)): BetroffeneStunde[] {
  const plan = tagesplan(d, iso, idx);
  const vertretungen = vertretungenAm(d, iso);
  const out: BetroffeneStunde[] = [];
  for (const p of plan) {
    const u = idx.unterricht.get(p.unterrichtId)!;
    const abwesend = u.lehrerIds.filter((l) => lehrerAbwesend(d, l, iso, p.stunde));
    const alleKlassenWeg = u.klassenIds.length > 0 && u.klassenIds.every((k) => klasseAbwesend(d, k, iso, p.stunde));
    const raumWeg = p.raumId ? raumGesperrt(d, p.raumId, iso, p.stunde) : undefined;
    let grund: Betroffenheit | null = null;
    if (alleKlassenWeg) grund = 'klasse';
    else if (abwesend.length > 0 && abwesend.length === u.lehrerIds.length) grund = 'lehrer';
    else if (raumWeg) grund = 'raum';
    const vorhandene = vertretungen.find((v) => v.planeintragId === p.id);
    if (!grund && !vorhandene) continue;
    out.push({
      planeintrag: p,
      unterricht: u,
      stunde: p.stunde,
      grund: grund ?? 'lehrer',
      abwesendeLehrerIds: abwesend,
      vertretung: vorhandene,
      entfallMoeglich: entfallMoeglich(d, idx, plan, u, p.stunde),
    });
  }
  return out.sort((a, b) => a.stunde - b.stunde || kurzKlassen(idx, a.unterricht.klassenIds).localeCompare(kurzKlassen(idx, b.unterricht.klassenIds)));
}

/** Entfall nur in Randstunden und ab einem bestimmten Jahrgang (Verlässlichkeit Sek I) */
function entfallMoeglich(d: SchoolData, idx: DataIndex, plan: Planeintrag[], u: Unterricht, stunde: number): boolean {
  const ab = jahrgangRang(d.einstellungen.entfallErlaubtAbJahrgang);
  return u.klassenIds.every((kid) => {
    const k = idx.klassen.get(kid);
    if (!k || jahrgangRang(k.jahrgang) < ab) return false;
    const stunden = plan.filter((p) => idx.unterricht.get(p.unterrichtId)?.klassenIds.includes(kid)).map((p) => p.stunde);
    return stunde === Math.min(...stunden) || stunde === Math.max(...stunden);
  });
}

export interface Kandidat {
  lehrerId: string;
  punkte: number;
  gruende: string[];
  /** Zählt als Mehrarbeit (Lehrkraft hätte regulär frei) */
  mehrarbeit: boolean;
}

/** Ist die Lehrkraft zu Tag/Stunde durch eigenen Unterricht, Vertretung oder Abwesenheit gebunden? */
export function lehrerBelegt(d: SchoolData, idx: DataIndex, lehrerId: string, iso: string, stunde: number, plan = tagesplan(d, iso, idx)): string | null {
  if (lehrerAbwesend(d, lehrerId, iso, stunde)) return 'abwesend';
  const l = idx.lehrer.get(lehrerId);
  if (l && hasSlot(l.sperrzeiten, weekdayIndex(iso), stunde)) return 'gesperrt';
  for (const v of vertretungenAm(d, iso)) {
    if (v.stunde === stunde && v.vertreterIds.includes(lehrerId) && v.art !== 'Entfall') return 'vertritt bereits';
  }
  for (const p of plan) {
    if (p.stunde !== stunde) continue;
    const u = idx.unterricht.get(p.unterrichtId)!;
    if (!u.lehrerIds.includes(lehrerId)) continue;
    const freigesetzt = u.klassenIds.length > 0 && u.klassenIds.every((k) => klasseAbwesend(d, k, iso, stunde));
    const ersetzt = vertretungenAm(d, iso).some((v) => v.planeintragId === p.id && ['Entfall', 'Freisetzung', 'Vertretung', 'EVA', 'Mitbetreuung'].includes(v.art) && !v.vertreterIds.includes(lehrerId));
    if (!freigesetzt && !ersetzt) return 'eigener Unterricht';
  }
  return null;
}

function freigesetztIn(d: SchoolData, idx: DataIndex, plan: Planeintrag[], lehrerId: string, iso: string, stunde: number): boolean {
  return plan.some((p) => {
    if (p.stunde !== stunde) return false;
    const u = idx.unterricht.get(p.unterrichtId)!;
    return u.lehrerIds.includes(lehrerId) && u.klassenIds.length > 0 && u.klassenIds.every((k) => klasseAbwesend(d, k, iso, stunde));
  });
}

export function vertretungenInWoche(d: SchoolData, lehrerId: string, iso: string): number {
  const mo = mondayOf(iso);
  const so = addDays(mo, 6);
  return d.vertretungen.filter((v) => v.datum >= mo && v.datum <= so && v.vertreterIds.includes(lehrerId) && v.art !== 'Entfall').length;
}

/** Bewertete Liste möglicher Vertretungslehrkräfte */
export function vertretungsVorschlaege(d: SchoolData, iso: string, u: Unterricht, stunde: number, idx: DataIndex = buildIndex(d)): Kandidat[] {
  const plan = tagesplan(d, iso, idx);
  const wd = weekdayIndex(iso);
  const out: Kandidat[] = [];
  for (const l of d.lehrer) {
    if (!l.aktiv || u.lehrerIds.includes(l.id)) continue;
    if (lehrerBelegt(d, idx, l.id, iso, stunde, plan)) continue;
    let punkte = 0;
    const gruende: string[] = [];
    let mehrarbeit = true;
    const eigeneStunden = new Set(
      plan.filter((p) => idx.unterricht.get(p.unterrichtId)!.lehrerIds.includes(l.id)).map((p) => p.stunde),
    );
    if (freigesetztIn(d, idx, plan, l.id, iso, stunde)) {
      punkte += 60;
      gruende.push('freigesetzt (Klasse abwesend)');
      mehrarbeit = false;
    }
    if (hasSlot(l.bereitschaft, wd, stunde)) {
      punkte += 45;
      gruende.push('Bereitschaft');
      mehrarbeit = false;
    }
    const kenntKlasse = d.unterricht.some((o) => o.lehrerIds.includes(l.id) && o.klassenIds.some((k) => u.klassenIds.includes(k)));
    if (kenntKlasse) {
      punkte += 20;
      gruende.push('unterrichtet Klasse');
    }
    if (l.faecher.includes(u.fachId)) {
      punkte += 15;
      gruende.push('Fachlehrkraft');
    }
    if (eigeneStunden.size === 0) {
      punkte -= 35;
      gruende.push('sonst unterrichtsfrei');
    } else if (eigeneStunden.has(stunde - 1) || eigeneStunden.has(stunde + 1)) {
      punkte += 12;
      gruende.push('Randstunde/anschließend');
    } else {
      const min = Math.min(...eigeneStunden);
      const max = Math.max(...eigeneStunden);
      if (stunde > min && stunde < max) {
        punkte += 8;
        gruende.push('Hohlstunde');
      } else {
        punkte -= 10;
        gruende.push('verlängert Tag');
      }
    }
    const woche = vertretungenInWoche(d, l.id, iso);
    punkte -= woche * 8;
    if (woche) gruende.push(`${woche} Vertr. diese Woche`);
    if (woche >= d.einstellungen.maxVertretungenProWoche) {
      punkte -= 40;
      gruende.push('Wochengrenze erreicht');
    }
    if (l.pflichtstunden < 20) {
      punkte -= 6;
      gruende.push('Teilzeit');
    }
    if (eigeneStunden.size + 1 > l.maxStundenProTag) {
      punkte -= 25;
      gruende.push('Tageshöchstgrenze');
    }
    out.push({ lehrerId: l.id, punkte, gruende, mehrarbeit });
  }
  return out.sort((a, b) => b.punkte - a.punkte);
}

/** Freie Räume zu einer Stunde (für Raumvertretungen) */
export function freieRaeume(d: SchoolData, iso: string, stunde: number, idx: DataIndex = buildIndex(d)): string[] {
  const belegt = new Set<string>();
  for (const p of tagesplan(d, iso, idx)) if (p.stunde === stunde && p.raumId) belegt.add(p.raumId);
  for (const v of vertretungenAm(d, iso)) {
    if (v.stunde !== stunde) continue;
    if (v.raumId) belegt.add(v.raumId);
    if (v.art === 'Entfall' || v.art === 'Freisetzung' || v.art === 'Verlegung') {
      const p = v.planeintragId ? idx.planeintraege.get(v.planeintragId) : undefined;
      if (p?.raumId && v.raumId !== p.raumId) belegt.delete(p.raumId);
    }
  }
  const wd = weekdayIndex(iso);
  return d.raeume
    .filter((r) => !belegt.has(r.id) && !raumGesperrt(d, r.id, iso, stunde) && !hasSlot(r.sperrzeiten, wd, stunde))
    .map((r) => r.id);
}

export function neueVertretung(p: Partial<Vertretung> & { datum: string; stunde: number; art: VertretungsArt }): Vertretung {
  const now = new Date().toISOString();
  return {
    id: newId('ve'),
    planeintragId: null,
    unterrichtId: null,
    vertreterIds: [],
    raumId: null,
    fachId: null,
    nachDatum: null,
    nachStunde: null,
    text: '',
    mehrarbeit: false,
    erstellt: now,
    geaendert: now,
    ...p,
  };
}

/** Automatische Vertretungsplanung für alle offenen Stunden eines Tages */
export function automatischeVertretung(d: SchoolData, iso: string): Vertretung[] {
  const idx = buildIndex(d);
  const neu: Vertretung[] = [];
  const work: SchoolData = { ...d, vertretungen: [...d.vertretungen] };
  for (const b of betroffeneStunden(work, iso, idx)) {
    if (b.vertretung) continue;
    let v: Vertretung;
    const base = { datum: iso, stunde: b.stunde, planeintragId: b.planeintrag.id, unterrichtId: b.unterricht.id, fachId: b.unterricht.fachId };
    if (b.grund === 'klasse') {
      v = neueVertretung({ ...base, art: 'Freisetzung', text: 'Klasse abwesend' });
    } else if (b.grund === 'raum') {
      const frei = freieRaeume(work, iso, b.stunde, idx);
      const fach = idx.faecher.get(b.unterricht.fachId);
      const passend = frei.find((r) => !fach?.raumTyp || idx.raeume.get(r)?.typ === fach.raumTyp) ?? frei[0] ?? null;
      v = neueVertretung({ ...base, art: 'Raumänderung', raumId: passend, vertreterIds: b.unterricht.lehrerIds });
    } else {
      const k = vertretungsVorschlaege(work, iso, b.unterricht, b.stunde, idx);
      if (b.entfallMoeglich && (k.length === 0 || k[0].punkte < 30)) {
        v = neueVertretung({ ...base, art: 'Entfall', text: 'Unterricht fällt aus' });
      } else if (k.length > 0) {
        v = neueVertretung({ ...base, art: 'Vertretung', vertreterIds: [k[0].lehrerId], raumId: b.planeintrag.raumId, mehrarbeit: k[0].mehrarbeit });
      } else {
        v = neueVertretung({ ...base, art: 'Mitbetreuung', text: 'Keine freie Lehrkraft – bitte manuell klären' });
      }
    }
    neu.push(v);
    work.vertretungen.push(v);
  }
  return neu;
}

export interface PlanZeile {
  stunde: number;
  klassen: string;
  fach: string;
  fachNeu: string;
  lehrer: string;
  vertreter: string;
  raum: string;
  raumNeu: string;
  art: VertretungsArt;
  text: string;
  klassenIds: string[];
  lehrerIds: string[];
  vertreterIds: string[];
}

/** Zeilen des Vertretungsplans (Aushang, Monitor, Web-Portal) */
export function vertretungsplanZeilen(d: SchoolData, iso: string, idx: DataIndex = buildIndex(d)): PlanZeile[] {
  const zeilen: PlanZeile[] = [];
  for (const v of vertretungenAm(d, iso)) {
    const p = v.planeintragId ? idx.planeintraege.get(v.planeintragId) : undefined;
    const u = v.unterrichtId ? idx.unterricht.get(v.unterrichtId) : p ? idx.unterricht.get(p.unterrichtId) : undefined;
    const fachAlt = u ? idx.faecher.get(u.fachId)?.kuerzel ?? '' : '';
    const fachNeu = v.fachId && v.fachId !== u?.fachId ? idx.faecher.get(v.fachId)?.kuerzel ?? '' : fachAlt;
    const raumAlt = p?.raumId ? idx.raeume.get(p.raumId)?.kuerzel ?? '' : '';
    const raumNeu = v.raumId ? idx.raeume.get(v.raumId)?.kuerzel ?? '' : v.art === 'Entfall' || v.art === 'Freisetzung' ? '' : raumAlt;
    let text = v.text;
    if (v.art === 'Verlegung' && v.nachDatum) text = `verlegt auf ${v.nachDatum.split('-').reverse().join('.')} ${v.nachStunde ?? ''}. Std. ${text}`.trim();
    zeilen.push({
      stunde: v.stunde,
      klassen: u ? kurzKlassen(idx, u.klassenIds) : '',
      fach: fachAlt,
      fachNeu,
      lehrer: u ? kurzLehrer(idx, u.lehrerIds) : '',
      vertreter: kurzLehrer(idx, v.vertreterIds),
      raum: raumAlt,
      raumNeu,
      art: v.art,
      text,
      klassenIds: u?.klassenIds ?? [],
      lehrerIds: u?.lehrerIds ?? [],
      vertreterIds: v.vertreterIds,
    });
  }
  return zeilen.sort((a, b) => a.klassen.localeCompare(b.klassen, 'de', { numeric: true }) || a.stunde - b.stunde);
}

/** Wochentag-Datum zum Wochentagsindex einer Woche */
export function datumInWoche(montag: string, tag: number): string {
  return addDays(montag, tag);
}

export function naechsterUnterrichtstag(d: SchoolData, iso: string): string {
  let x = addDays(iso, 1);
  for (let i = 0; i < 60 && !istUnterrichtstag(d, x); i++) x = addDays(x, 1);
  return x;
}

export function monatVon(iso: string): string {
  const dt = fromISO(iso);
  return `${dt.getFullYear()}-${String(dt.getMonth() + 1).padStart(2, '0')}`;
}
