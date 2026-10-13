// Statistiken: Deputat, Vertretungen, Mehrarbeit (NRW), Unterrichtsausfall, Stundentafel-Abgleich.

import { buildIndex, istStundenLehrer } from './index';
import { sollStunden, type SchoolData } from './model';
import { monatVon } from './vertretung';

export interface DeputatZeile {
  lehrerId: string;
  kuerzel: string;
  name: string;
  pflicht: number;
  anrechnung: number;
  soll: number;
  ist: number;
  verplant: number;
  differenz: number;
}

export function deputatsUebersicht(d: SchoolData, halbjahr: 1 | 2 = 1): DeputatZeile[] {
  const idx = buildIndex(d);
  return d.lehrer.map((l) => {
    const ist = istStundenLehrer(d, l.id, halbjahr);
    const verplant = d.stundenplan.filter((p) => idx.unterricht.get(p.unterrichtId)?.lehrerIds.includes(l.id)).length;
    const anrechnung = l.anrechnungen.reduce((s, a) => s + a.stunden, 0);
    return {
      lehrerId: l.id,
      kuerzel: l.kuerzel,
      name: `${l.nachname}, ${l.vorname}`,
      pflicht: l.pflichtstunden,
      anrechnung,
      soll: sollStunden(l),
      ist,
      verplant,
      differenz: ist - sollStunden(l),
    };
  });
}

export interface MehrarbeitZeile {
  lehrerId: string;
  kuerzel: string;
  monat: string;
  vertretungen: number;
  mehrarbeit: number;
  /** Ausgefallene eigene Stunden (z. B. durch Klassenfahrt/Freisetzung) – mindern ggf. die Mehrarbeit */
  minderarbeit: number;
  saldo: number;
  /** NRW: Vergütung, wenn mehr als die Grenze (Standard 3 Std.) im Kalendermonat geleistet wurde */
  verguetungsfaehig: number;
}

/**
 * Mehrarbeitsstatistik je Kalendermonat.
 * Hinweis: Im NRW-Schuldienst wird Mehrarbeit in der Regel erst vergütet, wenn im Kalendermonat mehr
 * als drei Unterrichtsstunden geleistet wurden (Grenze unter Einstellungen änderbar). Teilzeitkräfte und
 * aktuelle Erlasslage beachten. Die Auswertung ist eine Hilfe und ersetzt keine Prüfung durch die
 * Schulleitung bzw. Bezirksregierung.
 */
export function mehrarbeitsStatistik(d: SchoolData, monat: string): MehrarbeitZeile[] {
  const idx = buildIndex(d);
  const grenze = d.einstellungen.mehrarbeitsGrenze;
  const imMonat = d.vertretungen.filter((v) => monatVon(v.datum) === monat);
  return d.lehrer
    .map((l) => {
      const gehalten = imMonat.filter((v) => v.vertreterIds.includes(l.id) && !['Entfall', 'Freisetzung', 'Raumänderung'].includes(v.art));
      const mehrarbeit = gehalten.filter((v) => v.mehrarbeit).length;
      const minderarbeit = imMonat.filter((v) => {
        if (v.art !== 'Freisetzung' && v.art !== 'Entfall') return false;
        const u = v.unterrichtId ? idx.unterricht.get(v.unterrichtId) : undefined;
        return !!u?.lehrerIds.includes(l.id) && v.art === 'Freisetzung';
      }).length;
      const saldo = mehrarbeit - minderarbeit;
      return {
        lehrerId: l.id,
        kuerzel: l.kuerzel,
        monat,
        vertretungen: gehalten.length,
        mehrarbeit,
        minderarbeit,
        saldo,
        verguetungsfaehig: saldo > grenze ? saldo : 0,
      };
    })
    .filter((z) => z.vertretungen > 0 || z.minderarbeit > 0);
}

export interface AusfallStatistik {
  gesamt: number;
  vertreten: number;
  entfall: number;
  eva: number;
  freisetzung: number;
  raumaenderung: number;
  quoteAusfall: number;
}

export function ausfallStatistik(d: SchoolData, von: string, bis: string): AusfallStatistik {
  const vs = d.vertretungen.filter((v) => v.datum >= von && v.datum <= bis);
  const c = (a: string) => vs.filter((v) => v.art === a).length;
  const gesamt = vs.length;
  const entfall = c('Entfall');
  return {
    gesamt,
    vertreten: c('Vertretung') + c('Mitbetreuung') + c('Sondereinsatz'),
    entfall,
    eva: c('EVA'),
    freisetzung: c('Freisetzung'),
    raumaenderung: c('Raumänderung'),
    quoteAusfall: gesamt ? entfall / gesamt : 0,
  };
}

export interface StundentafelAbgleich {
  klasseId: string;
  klasse: string;
  fach: string;
  soll: number;
  ist: number;
}

/** Vergleicht Unterrichtsverteilung mit der Stundentafel des Jahrgangs */
export function stundentafelAbgleich(d: SchoolData): StundentafelAbgleich[] {
  const idx = buildIndex(d);
  const out: StundentafelAbgleich[] = [];
  for (const k of d.klassen) {
    const zeilen = d.stundentafel.filter((z) => z.jahrgang === k.jahrgang);
    const faecher = new Set([...zeilen.map((z) => z.fachId), ...d.unterricht.filter((u) => u.klassenIds.includes(k.id)).map((u) => u.fachId)]);
    for (const fid of faecher) {
      const soll = zeilen.find((z) => z.fachId === fid)?.stunden ?? 0;
      const ist = d.unterricht.filter((u) => u.klassenIds.includes(k.id) && u.fachId === fid && !u.gruppe).reduce((s, u) => s + u.wochenstunden, 0)
        + Math.max(0, ...d.unterricht.filter((u) => u.klassenIds.includes(k.id) && u.fachId === fid && u.gruppe).map((u) => u.wochenstunden));
      if (soll !== ist) out.push({ klasseId: k.id, klasse: k.name, fach: idx.faecher.get(fid)?.kuerzel ?? '?', soll, ist });
    }
  }
  return out;
}
