// Erzeugt neue (leere) Schuldateien sowie eine Beispielschule zum Ausprobieren.

import { FAECHER_NRW, FERIEN_NRW, PFLICHTSTUNDEN, feiertageNRW, standardZeitraster } from './nrw';
import {
  DATA_VERSION,
  newId,
  type Fach,
  type Klasse,
  type Lehrer,
  type Raum,
  type SchoolData,
  type Schulform,
  type Unterricht,
} from './model';

export function emptySchool(schulform: Schulform = 'GY', schulname = 'Neue Schule'): SchoolData {
  const startYear = 2026;
  const freieTage = [
    ...FERIEN_NRW,
    ...feiertageNRW(startYear),
    ...feiertageNRW(startYear + 1),
  ].map((t) => ({ ...t, id: newId('ft') }));
  return {
    dataVersion: DATA_VERSION,
    meta: {
      schulname,
      schulnummer: '',
      schulform,
      ort: '',
      schuljahr: `${startYear}/${String((startYear + 1) % 100).padStart(2, '0')}`,
      schuljahrBeginn: '2026-09-02',
      schuljahrEnde: '2027-07-16',
      halbjahrWechsel: '2027-02-01',
      bundesland: 'NW',
    },
    zeitraster: { tage: 5, stunden: standardZeitraster(), mittagspauseStunde: 7 },
    lehrer: [],
    klassen: [],
    raeume: [],
    faecher: FAECHER_NRW.map((fa) => ({ ...fa, id: newId('fa') })),
    unterricht: [],
    stundenplan: [],
    pausen: [
      { id: newId('pa'), name: '1. große Pause', nachStunde: 2, dauer: 20 },
      { id: newId('pa'), name: '2. große Pause', nachStunde: 4, dauer: 20 },
      { id: newId('pa'), name: 'Mittagspause', nachStunde: 6, dauer: 5 },
    ],
    aufsichtsbereiche: [
      { id: newId('ab'), name: 'Schulhof', bedarf: 2 },
      { id: newId('ab'), name: 'Pausenhalle', bedarf: 1 },
    ],
    aufsichten: [],
    abwesenheiten: [],
    vertretungen: [],
    freieTage,
    tagesnachrichten: [],
    stundentafel: [],
    sync: {
      portalUrl: '',
      schulId: '',
      apiKey: '',
      autoSync: false,
      tageVoraus: 7,
      letzteSynchronisation: null,
      letzteRevision: 0,
    },
    einstellungen: {
      mehrarbeitsGrenze: 3,
      entfallErlaubtAbJahrgang: 'EF',
      maxVertretungenProWoche: 3,
      vertretungsplanTitel: 'Vertretungsplan',
    },
  };
}

/** Stellt sicher, dass ältere oder unvollständige Dateien alle Felder besitzen. */
export function migrate(raw: any): SchoolData {
  const base = emptySchool(raw?.meta?.schulform ?? 'GY');
  const data: SchoolData = { ...base, ...raw };
  data.meta = { ...base.meta, ...(raw?.meta ?? {}) };
  data.zeitraster = { ...base.zeitraster, ...(raw?.zeitraster ?? {}) };
  data.sync = { ...base.sync, ...(raw?.sync ?? {}) };
  data.einstellungen = { ...base.einstellungen, ...(raw?.einstellungen ?? {}) };
  for (const key of [
    'lehrer', 'klassen', 'raeume', 'faecher', 'unterricht', 'stundenplan', 'pausen',
    'aufsichtsbereiche', 'aufsichten', 'abwesenheiten', 'vertretungen', 'freieTage',
    'tagesnachrichten', 'stundentafel',
  ] as const) {
    if (!Array.isArray((data as any)[key])) (data as any)[key] = (base as any)[key];
  }
  data.lehrer = data.lehrer.map((l) => ({ ...newLehrer(), ...l }));
  data.klassen = data.klassen.map((k) => ({ ...newKlasse(), ...k }));
  data.raeume = data.raeume.map((r) => ({ ...newRaum(), ...r }));
  data.unterricht = data.unterricht.map((u) => ({ ...newUnterricht(), ...u }));
  data.dataVersion = DATA_VERSION;
  return data;
}

export function newLehrer(p: Partial<Lehrer> = {}): Lehrer {
  return {
    id: newId('le'),
    kuerzel: '',
    nachname: '',
    vorname: '',
    email: '',
    pflichtstunden: 25.5,
    anrechnungen: [],
    faecher: [],
    sperrzeiten: [],
    maxStundenProTag: 7,
    bereitschaft: [],
    aktiv: true,
    notiz: '',
    ...p,
  };
}

export function newKlasse(p: Partial<Klasse> = {}): Klasse {
  return {
    id: newId('kl'),
    name: '',
    jahrgang: '5',
    klassenlehrerIds: [],
    raumId: null,
    schuelerzahl: 28,
    sperrzeiten: [],
    keineHohlstunden: true,
    ...p,
  };
}

export function newRaum(p: Partial<Raum> = {}): Raum {
  return { id: newId('ra'), kuerzel: '', name: '', typ: 'Klassenraum', kapazitaet: 30, sperrzeiten: [], ...p };
}

export function newFach(p: Partial<Fach> = {}): Fach {
  return { id: newId('fa'), kuerzel: '', name: '', farbe: '#cccccc', raumTyp: null, hauptfach: false, maxProTag: 2, ...p };
}

export function newUnterricht(p: Partial<Unterricht> = {}): Unterricht {
  return {
    id: newId('un'),
    fachId: '',
    lehrerIds: [],
    klassenIds: [],
    wochenstunden: 2,
    doppelstunden: 0,
    raumIds: [],
    gruppe: '',
    kursart: null,
    kursname: '',
    schiene: '',
    halbjahr: 0,
    notiz: '',
    ...p,
  };
}

// ---------------------------------------------------------------------------
// Beispielschule (fiktives Gymnasium) zum Testen aller Funktionen
// ---------------------------------------------------------------------------

const NACHNAMEN = [
  'Müller', 'Schmidt', 'Schneider', 'Fischer', 'Weber', 'Meyer', 'Wagner', 'Becker', 'Schulz', 'Hoffmann',
  'Koch', 'Richter', 'Klein', 'Wolf', 'Schröder', 'Neumann', 'Schwarz', 'Braun', 'Zimmermann', 'Krüger',
  'Hartmann', 'Lange', 'Werner', 'Krause', 'Lehmann', 'Köhler', 'Maier', 'Herrmann', 'Walter', 'König',
  'Kaiser', 'Fuchs', 'Peters', 'Lang', 'Scholz', 'Möller',
];
const VORNAMEN = [
  'Anna', 'Thomas', 'Julia', 'Michael', 'Sabine', 'Andreas', 'Katrin', 'Stefan', 'Nina', 'Markus',
  'Laura', 'Jan', 'Claudia', 'Tobias', 'Sarah', 'Frank', 'Lena', 'Daniel', 'Petra', 'Christian',
  'Miriam', 'Jörg', 'Eva', 'Sebastian', 'Ute', 'Kai', 'Birgit', 'Lars', 'Monika', 'Ralf',
  'Svenja', 'Dirk', 'Heike', 'Uwe', 'Ines', 'Volker',
];

/** Stundentafel der Beispielschule (vereinfachte Vorlage, schulintern anpassbar) */
const BEISPIEL_STUNDENTAFEL: Record<string, Record<string, number>> = {
  '5': { D: 4, M: 4, E: 4, BI: 2, EK: 2, GE: 1, PK: 1, KR: 2, KU: 2, MU: 2, SP: 3, KL: 1 },
  '6': { D: 4, M: 4, E: 4, F: 3, BI: 2, PH: 2, GE: 2, KR: 2, KU: 2, MU: 1, SP: 3 },
  '7': { D: 4, M: 4, E: 3, F: 3, BI: 2, PH: 2, CH: 2, EK: 2, GE: 2, KR: 2, KU: 1, SP: 3 },
};

export function demoSchool(): SchoolData {
  const d = emptySchool('GY', 'Städtisches Beispiel-Gymnasium');
  d.meta.schulnummer = '199999';
  d.meta.ort = 'Musterstadt';
  d.zeitraster.stunden = d.zeitraster.stunden.slice(0, 8);
  const fach = (k: string) => d.faecher.find((x) => x.kuerzel === k)!;

  // Räume
  for (let i = 1; i <= 14; i++) d.raeume.push(newRaum({ kuerzel: `R${100 + i}`, name: `Raum ${100 + i}` }));
  d.raeume.push(newRaum({ kuerzel: 'BI1', name: 'Biologie-Fachraum', typ: 'Fachraum' }));
  d.raeume.push(newRaum({ kuerzel: 'CH1', name: 'Chemie-Fachraum', typ: 'Fachraum' }));
  d.raeume.push(newRaum({ kuerzel: 'PH1', name: 'Physik-Fachraum', typ: 'Fachraum' }));
  d.raeume.push(newRaum({ kuerzel: 'KU1', name: 'Kunstraum', typ: 'Kunstraum' }));
  d.raeume.push(newRaum({ kuerzel: 'MU1', name: 'Musikraum', typ: 'Musikraum' }));
  d.raeume.push(newRaum({ kuerzel: 'TH1', name: 'Turnhalle 1', typ: 'Sporthalle', kapazitaet: 60 }));
  d.raeume.push(newRaum({ kuerzel: 'TH2', name: 'Turnhalle 2', typ: 'Sporthalle', kapazitaet: 60 }));
  d.raeume.push(newRaum({ kuerzel: 'IF1', name: 'Computerraum', typ: 'Computerraum' }));

  // Klassen 5a–7c
  const klassen: Klasse[] = [];
  let r = 0;
  for (const jg of ['5', '6', '7']) {
    for (const z of ['a', 'b', 'c']) {
      klassen.push(newKlasse({ name: `${jg}${z}`, jahrgang: jg, raumId: d.raeume[r++].id, schuelerzahl: 26 + (r % 5) }));
    }
  }
  d.klassen = klassen;

  // Stundentafel
  for (const [jg, row] of Object.entries(BEISPIEL_STUNDENTAFEL)) {
    for (const [fk, std] of Object.entries(row)) d.stundentafel.push({ jahrgang: jg, fachId: fach(fk).id, stunden: std });
  }

  // Lehrkräfte mit zwei Fächern
  const kombis = [
    ['D', 'GE'], ['M', 'PH'], ['E', 'SP'], ['D', 'E'], ['M', 'BI'], ['BI', 'CH'], ['F', 'E'], ['D', 'KR'],
    ['M', 'EK'], ['KU', 'D'], ['MU', 'M'], ['SP', 'BI'], ['GE', 'PK'], ['E', 'GE'], ['D', 'PK'], ['M', 'CH'],
    ['F', 'D'], ['SP', 'EK'], ['KR', 'GE'], ['PH', 'M'], ['E', 'KR'], ['D', 'MU'], ['M', 'SP'], ['KU', 'E'],
    ['BI', 'EK'], ['D', 'SP'], ['E', 'M'], ['F', 'GE'],
  ];
  const lehrer: Lehrer[] = kombis.map((fs, i) => {
    const nachname = NACHNAMEN[i];
    return newLehrer({
      kuerzel: (nachname.slice(0, 3) + (i > 25 ? i : '')).toUpperCase().replace('Ü', 'UE').replace('Ö', 'OE').slice(0, 4),
      nachname,
      vorname: VORNAMEN[i],
      email: `${nachname.toLowerCase().replace('ü', 'ue').replace('ö', 'oe')}@beispiel-gymnasium.de`,
      pflichtstunden: i % 7 === 3 ? 18 : PFLICHTSTUNDEN.GY,
      anrechnungen: i === 0 ? [{ grund: 'Schulleitung', stunden: 10 }] : i === 1 ? [{ grund: 'Stellv. Schulleitung', stunden: 7 }] : [],
      faecher: fs.map((k) => fach(k).id),
      bereitschaft: [{ tag: i % 5, stunde: 3 + (i % 3) }],
    });
  });
  // eindeutige Kürzel sicherstellen
  const seen = new Set<string>();
  for (const l of lehrer) {
    let k = l.kuerzel;
    let n = 1;
    while (seen.has(k)) k = l.kuerzel.slice(0, 3) + n++;
    l.kuerzel = k;
    seen.add(k);
  }
  d.lehrer = lehrer;

  // Unterrichtsverteilung nach Stundentafel, Lehrkräfte nach Auslastung zuordnen
  const last = new Map<string, number>();
  const pick = (fachId: string, exclude: string[] = []) => {
    const cands = lehrer
      .filter((l) => l.faecher.includes(fachId) && !exclude.includes(l.id))
      .sort((a, b) => (last.get(a.id) ?? 0) / (a.pflichtstunden - 6) - (last.get(b.id) ?? 0) / (b.pflichtstunden - 6));
    return cands[0];
  };
  for (const k of klassen) {
    const row = BEISPIEL_STUNDENTAFEL[k.jahrgang];
    for (const [fk, std] of Object.entries(row)) {
      const fa = fach(fk);
      const l = pick(fa.id) ?? lehrer[0];
      last.set(l.id, (last.get(l.id) ?? 0) + std);
      const raumIds = fa.raumTyp ? d.raeume.filter((x) => x.typ === fa.raumTyp).map((x) => x.id) : [];
      d.unterricht.push(
        newUnterricht({
          fachId: fa.id,
          lehrerIds: [l.id],
          klassenIds: [k.id],
          wochenstunden: std,
          doppelstunden: fk === 'SP' || fk === 'KU' ? 1 : 0,
          raumIds,
        }),
      );
    }
  }
  for (const k of klassen) {
    const kl = d.unterricht.find((u) => u.klassenIds[0] === k.id && fach('D').id === u.fachId);
    if (kl) k.klassenlehrerIds = [kl.lehrerIds[0]];
  }

  d.tagesnachrichten.push({ id: newId('tn'), datum: '2026-10-14', text: 'Lehrerkonferenz um 14:15 Uhr in der Aula.', intern: true });
  return d;
}
