import { describe, expect, it } from 'vitest';
import { demoSchool, emptySchool, migrate, newUnterricht } from '../src/core/factory';
import { easterSunday, isoWeek, weekdayIndex, parseDE, formatDE } from '../src/core/dates';
import { feiertageNRW, schuljahrFuer } from '../src/core/nrw';
import { erstelleStundenplan } from '../src/core/planer';
import { pruefeStundenplan } from '../src/core/pruefung';
import {
  automatischeVertretung, betroffeneStunden, istUnterrichtstag, vertretungsVorschlaege, vertretungsplanZeilen,
} from '../src/core/vertretung';
import { mehrarbeitsStatistik, deputatsUebersicht, stundentafelAbgleich } from '../src/core/statistik';
import { exportGPU, importGPU, importLehrerCSV, parseCSV, toCSV, exportICal } from '../src/core/austausch';
import { buildSyncPayload, uebernehmeMeldungen } from '../src/core/syncPayload';
import { verteileAufsichten } from '../src/core/aufsicht';
import { newId } from '../src/core/model';

describe('Datum & NRW', () => {
  it('berechnet Ostern korrekt', () => {
    expect(easterSunday(2026)).toBe('2026-04-05');
    expect(easterSunday(2027)).toBe('2027-03-28');
    expect(easterSunday(2024)).toBe('2024-03-31');
  });
  it('liefert NRW-Feiertage inkl. Fronleichnam und Allerheiligen', () => {
    const f = feiertageNRW(2026);
    expect(f.find((x) => x.name === 'Fronleichnam')?.von).toBe('2026-06-04');
    expect(f.find((x) => x.name === 'Allerheiligen')?.von).toBe('2026-11-01');
    expect(f).toHaveLength(11);
  });
  it('Wochentage, KW, Formate', () => {
    expect(weekdayIndex('2026-10-12')).toBe(0);
    expect(isoWeek('2026-10-13')).toBe(42);
    expect(parseDE('3.10.26')).toBe('2026-10-03');
    expect(formatDE('2026-10-03')).toBe('03.10.2026');
    expect(schuljahrFuer('2026-10-13')).toBe('2026/27');
    expect(schuljahrFuer('2027-03-01')).toBe('2026/27');
  });
  it('kennt Herbstferien NRW 2026 als unterrichtsfrei', () => {
    const d = emptySchool();
    expect(istUnterrichtstag(d, '2026-10-19')).toBe(false);
    expect(istUnterrichtstag(d, '2026-10-13')).toBe(true);
    expect(istUnterrichtstag(d, '2026-10-03')).toBe(false);
  });
});

describe('Automatische Stundenplanung', () => {
  const d = demoSchool();
  const res = erstelleStundenplan(d, { iterationen: 60_000, seed: 7 });
  d.stundenplan = res.stundenplan;

  it('verplant alle Stunden', () => {
    const soll = d.unterricht.reduce((s, u) => s + u.wochenstunden, 0);
    expect(d.stundenplan.length).toBe(soll);
  });
  it('erzeugt einen Plan ohne harte Konflikte', () => {
    expect(res.harteKonflikte).toBe(0);
    const fehler = pruefeStundenplan(d).filter((b) => b.schwere === 'fehler');
    expect(fehler).toEqual([]);
  });
  it('respektiert fixierte Einträge und Sperrzeiten', () => {
    const d2 = demoSchool();
    const u = d2.unterricht[0];
    d2.stundenplan = [{ id: newId('pl'), unterrichtId: u.id, tag: 2, stunde: 3, raumId: null, fixiert: true }];
    const l = d2.lehrer.find((x) => x.id === d2.unterricht[1].lehrerIds[0])!;
    l.sperrzeiten = [0, 1, 2, 3, 4].map((tag) => ({ tag, stunde: 1 }));
    const r = erstelleStundenplan(d2, { iterationen: 40_000 });
    expect(r.stundenplan.some((p) => p.unterrichtId === u.id && p.tag === 2 && p.stunde === 3 && p.fixiert)).toBe(true);
    const ids = new Set(d2.unterricht.filter((x) => x.lehrerIds.includes(l.id)).map((x) => x.id));
    expect(r.stundenplan.filter((p) => ids.has(p.unterrichtId) && p.stunde === 1)).toHaveLength(0);
  });
  it('legt Schienen (Kurse) parallel', () => {
    const d3 = demoSchool();
    const k = d3.klassen[0];
    const re = d3.faecher.find((f) => f.kuerzel === 'KR')!;
    const er = d3.faecher.find((f) => f.kuerzel === 'ER')!;
    d3.unterricht = d3.unterricht.filter((u) => !(u.klassenIds.includes(k.id) && u.fachId === re.id));
    const a = newUnterricht({ fachId: re.id, klassenIds: [k.id], lehrerIds: [d3.lehrer[7].id], wochenstunden: 2, schiene: 'REL5', gruppe: 'KR' });
    const b = newUnterricht({ fachId: er.id, klassenIds: [k.id], lehrerIds: [d3.lehrer[20].id], wochenstunden: 2, schiene: 'REL5', gruppe: 'ER' });
    d3.unterricht.push(a, b);
    const r = erstelleStundenplan(d3, { iterationen: 40_000 });
    const pa = r.stundenplan.filter((p) => p.unterrichtId === a.id).map((p) => `${p.tag}:${p.stunde}`).sort();
    const pb = r.stundenplan.filter((p) => p.unterrichtId === b.id).map((p) => `${p.tag}:${p.stunde}`).sort();
    expect(pa).toEqual(pb);
    d3.stundenplan = r.stundenplan;
    expect(pruefeStundenplan(d3).filter((x) => x.kategorie === 'Klassen-Kollision')).toEqual([]);
  });
});

describe('Vertretungsplanung', () => {
  const d = demoSchool();
  d.stundenplan = erstelleStundenplan(d, { iterationen: 40_000 }).stundenplan;
  const datum = '2026-10-13'; // Dienstag
  const krank = d.lehrer[3];
  d.abwesenheiten.push({ id: 'a1', typ: 'lehrer', refId: krank.id, von: datum, bis: datum, stundeVon: 1, stundeBis: 8, grund: 'Krankheit', text: '', begleitLehrerIds: [] });

  it('findet betroffene Stunden', () => {
    const b = betroffeneStunden(d, datum);
    const erwartet = d.stundenplan.filter((p) => p.tag === 1 && d.unterricht.find((u) => u.id === p.unterrichtId)!.lehrerIds.includes(krank.id));
    expect(b.length).toBe(erwartet.length);
    expect(b.every((x) => x.grund === 'lehrer')).toBe(true);
  });
  it('schlägt nur freie Lehrkräfte vor', () => {
    const b = betroffeneStunden(d, datum)[0];
    const k = vertretungsVorschlaege(d, datum, b.unterricht, b.stunde);
    expect(k.length).toBeGreaterThan(0);
    for (const c of k) {
      const belegt = d.stundenplan.some((p) => p.tag === 1 && p.stunde === b.stunde && d.unterricht.find((u) => u.id === p.unterrichtId)!.lehrerIds.includes(c.lehrerId));
      expect(belegt).toBe(false);
    }
  });
  it('plant automatisch ohne Doppelbelegung der Vertreter', () => {
    const neu = automatischeVertretung(d, datum);
    expect(neu.length).toBe(betroffeneStunden(d, datum).length);
    const keys = neu.filter((v) => v.art === 'Vertretung').flatMap((v) => v.vertreterIds.map((l) => `${l}:${v.stunde}`));
    expect(new Set(keys).size).toBe(keys.length);
    d.vertretungen.push(...neu);
    expect(vertretungsplanZeilen(d, datum).length).toBe(neu.length);
    expect(betroffeneStunden(d, datum).every((x) => x.vertretung)).toBe(true);
  });
  it('erkennt Freisetzung bei Klassenfahrt', () => {
    const d2 = structuredClone(d);
    d2.abwesenheiten = [];
    d2.vertretungen = [];
    const k = d2.klassen[0];
    d2.abwesenheiten.push({ id: 'a2', typ: 'klasse', refId: k.id, von: datum, bis: datum, stundeVon: 1, stundeBis: 8, grund: 'Klassenfahrt', text: '', begleitLehrerIds: [] });
    const b = betroffeneStunden(d2, datum);
    expect(b.length).toBeGreaterThan(0);
    expect(b.every((x) => x.grund === 'klasse')).toBe(true);
    const neu = automatischeVertretung(d2, datum);
    expect(neu.every((v) => v.art === 'Freisetzung')).toBe(true);
  });
  it('berechnet Mehrarbeit pro Monat', () => {
    const d2 = structuredClone(d);
    const l = d2.lehrer[5];
    for (let i = 0; i < 4; i++)
      d2.vertretungen.push({ id: 'v' + i, datum: '2026-11-0' + (2 + i), stunde: 1, planeintragId: null, unterrichtId: null, art: 'Vertretung', vertreterIds: [l.id], raumId: null, fachId: null, nachDatum: null, nachStunde: null, text: '', mehrarbeit: true, erstellt: '', geaendert: '' });
    const z = mehrarbeitsStatistik(d2, '2026-11').find((x) => x.lehrerId === l.id)!;
    expect(z.mehrarbeit).toBe(4);
    expect(z.verguetungsfaehig).toBe(4);
  });
});

describe('Import/Export & Sync', () => {
  it('parst CSV mit Semikolon und Anführungszeichen', () => {
    const rows = parseCSV('a;b;c\r\n"x;y";"he said ""hi""";3\n');
    expect(rows).toEqual([['a', 'b', 'c'], ['x;y', 'he said "hi"', '3']]);
    expect(parseCSV(toCSV([['1', 'a;b']]))).toEqual([['1', 'a;b']]);
  });
  it('importiert Lehrkräfte aus Schild-CSV', () => {
    const d = emptySchool();
    const r = importLehrerCSV(d, 'Kürzel;Nachname;Vorname;Pflichtstunden;Fächer\nMUE;Müller;Anna;25,5;D GE\nSCH;Schmidt;Tom;14;M\n');
    expect(r.neu).toBe(2);
    expect(d.lehrer[0].pflichtstunden).toBe(25.5);
    expect(d.lehrer[0].faecher).toHaveLength(2);
  });
  it('GPU-Export lässt sich wieder importieren', () => {
    const d = demoSchool();
    d.stundenplan = erstelleStundenplan(d, { iterationen: 5_000 }).stundenplan;
    const files = exportGPU(d);
    const e = emptySchool();
    const r = importGPU(e, files);
    expect(r.fehler).toEqual([]);
    expect(e.unterricht.length).toBe(d.unterricht.length);
    expect(e.stundenplan.length).toBe(d.stundenplan.length);
    expect(e.lehrer.length).toBe(d.lehrer.length);
  });
  it('erzeugt iCal und Sync-Paket', () => {
    const d = demoSchool();
    d.stundenplan = erstelleStundenplan(d, { iterationen: 5_000 }).stundenplan;
    const ics = exportICal(d, d.lehrer[2].id, '2026-10-12', '2026-10-16');
    expect(ics).toContain('BEGIN:VEVENT');
    const p = buildSyncPayload(d, '2026-10-12');
    expect(p.schema).toBe('untplan-sync/1');
    expect(p.stundenplan.length).toBe(d.stundenplan.length);
    const m = uebernehmeMeldungen(d, [{ id: 'm1', typ: 'abwesenheit', erstellt: '', absender: d.lehrer[0].kuerzel, von: '2026-10-14' }]);
    expect(m.uebernommen).toEqual(['m1']);
    expect(d.abwesenheiten).toHaveLength(1);
  });
  it('migriert unvollständige Dateien', () => {
    const d = migrate({ meta: { schulname: 'X' }, lehrer: [{ id: 'l', kuerzel: 'AB' }] });
    expect(d.lehrer[0].sperrzeiten).toEqual([]);
    expect(d.zeitraster.stunden.length).toBeGreaterThan(0);
  });
  it('Statistiken & Aufsichten', () => {
    const d = demoSchool();
    d.stundenplan = erstelleStundenplan(d, { iterationen: 5_000 }).stundenplan;
    expect(deputatsUebersicht(d)).toHaveLength(d.lehrer.length);
    expect(stundentafelAbgleich(d)).toEqual([]);
    const a = verteileAufsichten(d);
    expect(a.length).toBeGreaterThan(0);
    const keys = a.map((x) => `${x.lehrerId}:${x.tag}:${x.pauseId}`);
    expect(new Set(keys).size).toBe(keys.length);
  });
});
