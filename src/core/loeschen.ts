// Löschen von Stammdaten inkl. aller Verweise (referentielle Integrität).

import type { SchoolData } from './model';

export function loescheLehrer(d: SchoolData, id: string) {
  d.lehrer = d.lehrer.filter((l) => l.id !== id);
  for (const u of d.unterricht) u.lehrerIds = u.lehrerIds.filter((x) => x !== id);
  for (const k of d.klassen) k.klassenlehrerIds = k.klassenlehrerIds.filter((x) => x !== id);
  d.aufsichten = d.aufsichten.filter((a) => a.lehrerId !== id);
  d.abwesenheiten = d.abwesenheiten.filter((a) => !(a.typ === 'lehrer' && a.refId === id));
  for (const a of d.abwesenheiten) a.begleitLehrerIds = a.begleitLehrerIds.filter((x) => x !== id);
  for (const v of d.vertretungen) v.vertreterIds = v.vertreterIds.filter((x) => x !== id);
}

export function loescheKlasse(d: SchoolData, id: string) {
  d.klassen = d.klassen.filter((k) => k.id !== id);
  for (const u of d.unterricht) u.klassenIds = u.klassenIds.filter((x) => x !== id);
  // Unterricht ohne Klasse entfernen
  const leer = new Set(d.unterricht.filter((u) => u.klassenIds.length === 0).map((u) => u.id));
  loescheUnterrichte(d, leer);
  d.abwesenheiten = d.abwesenheiten.filter((a) => !(a.typ === 'klasse' && a.refId === id));
}

export function loescheRaum(d: SchoolData, id: string) {
  d.raeume = d.raeume.filter((r) => r.id !== id);
  for (const k of d.klassen) if (k.raumId === id) k.raumId = null;
  for (const u of d.unterricht) u.raumIds = u.raumIds.filter((x) => x !== id);
  for (const p of d.stundenplan) if (p.raumId === id) p.raumId = null;
  for (const v of d.vertretungen) if (v.raumId === id) v.raumId = null;
  d.abwesenheiten = d.abwesenheiten.filter((a) => !(a.typ === 'raum' && a.refId === id));
}

export function loescheFach(d: SchoolData, id: string) {
  d.faecher = d.faecher.filter((f) => f.id !== id);
  for (const l of d.lehrer) l.faecher = l.faecher.filter((x) => x !== id);
  d.stundentafel = d.stundentafel.filter((z) => z.fachId !== id);
  loescheUnterrichte(d, new Set(d.unterricht.filter((u) => u.fachId === id).map((u) => u.id)));
}

export function loescheUnterrichte(d: SchoolData, ids: Set<string>) {
  if (!ids.size) return;
  d.unterricht = d.unterricht.filter((u) => !ids.has(u.id));
  const plan = new Set(d.stundenplan.filter((p) => ids.has(p.unterrichtId)).map((p) => p.id));
  d.stundenplan = d.stundenplan.filter((p) => !plan.has(p.id));
  d.vertretungen = d.vertretungen.filter((v) => !(v.planeintragId && plan.has(v.planeintragId)));
}

/** Wird ein Fach/Klasse/Lehrer noch verwendet? (für Löschhinweise) */
export function verwendung(d: SchoolData, typ: 'lehrer' | 'klasse' | 'raum' | 'fach', id: string): number {
  switch (typ) {
    case 'lehrer': return d.unterricht.filter((u) => u.lehrerIds.includes(id)).length;
    case 'klasse': return d.unterricht.filter((u) => u.klassenIds.includes(id)).length;
    case 'raum': return d.stundenplan.filter((p) => p.raumId === id).length;
    case 'fach': return d.unterricht.filter((u) => u.fachId === id).length;
  }
}
