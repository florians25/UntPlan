// Aufbau des Sync-Pakets und Übernahme von Portal-Meldungen.

import { addDays, todayISO } from './dates';
import { buildIndex } from './index';
import { newId, type AbwesenheitsGrund, type SchoolData } from './model';
import { SCHULFORMEN } from './nrw';
import { SYNC_SCHEMA, type PortalMeldung, type SyncPayload } from './sync';
import { istUnterrichtstag, vertretungsplanZeilen } from './vertretung';

export function buildSyncPayload(d: SchoolData, ab: string = todayISO()): SyncPayload {
  const idx = buildIndex(d);
  const k = (id: string) => idx.lehrer.get(id)?.kuerzel ?? '';
  const vertretungen: SyncPayload['vertretungen'] = [];
  for (let i = 0; i < Math.max(1, d.sync.tageVoraus); i++) {
    const iso = addDays(ab, i);
    if (!istUnterrichtstag(d, iso)) continue;
    for (const z of vertretungsplanZeilen(d, iso, idx)) {
      vertretungen.push({
        datum: iso,
        stunde: z.stunde,
        art: z.art,
        klassen: z.klassenIds.map((id) => idx.klassen.get(id)?.name ?? ''),
        fach: z.fachNeu,
        lehrer: z.lehrerIds.map(k),
        vertreter: z.vertreterIds.map(k),
        raum: z.raumNeu,
        text: z.text,
      });
    }
  }
  return {
    schema: SYNC_SCHEMA,
    erzeugt: new Date().toISOString(),
    basisRevision: d.sync.letzteRevision,
    schule: {
      name: d.meta.schulname,
      schulnummer: d.meta.schulnummer,
      schulform: SCHULFORMEN[d.meta.schulform],
      schuljahr: d.meta.schuljahr,
      ort: d.meta.ort,
    },
    zeitraster: { tage: d.zeitraster.tage, stunden: d.zeitraster.stunden },
    lehrer: d.lehrer.filter((l) => l.aktiv).map((l) => ({ kuerzel: l.kuerzel, name: `${l.vorname} ${l.nachname}`.trim(), email: l.email })),
    klassen: d.klassen.map((c) => ({ name: c.name, jahrgang: c.jahrgang, klassenleitung: c.klassenlehrerIds.map(k) })),
    raeume: d.raeume.map((r) => ({ kuerzel: r.kuerzel, name: r.name })),
    faecher: d.faecher.map((f) => ({ kuerzel: f.kuerzel, name: f.name, farbe: f.farbe })),
    stundenplan: d.stundenplan.flatMap((p) => {
      const u = idx.unterricht.get(p.unterrichtId);
      if (!u) return [];
      return [{
        tag: p.tag,
        stunde: p.stunde,
        fach: idx.faecher.get(u.fachId)?.kuerzel ?? '',
        klassen: u.klassenIds.map((id) => idx.klassen.get(id)?.name ?? ''),
        lehrer: u.lehrerIds.map(k),
        raum: p.raumId ? idx.raeume.get(p.raumId)?.kuerzel ?? '' : '',
        gruppe: u.kursname || u.gruppe,
      }];
    }),
    vertretungen,
    nachrichten: d.tagesnachrichten.filter((n) => n.datum >= ab).map((n) => ({ datum: n.datum, text: n.text, intern: n.intern })),
    freieTage: d.freieTage.map((f) => ({ name: f.name, von: f.von, bis: f.bis, art: f.art })),
  };
}

const GRUENDE: AbwesenheitsGrund[] = ['Krankheit', 'Fortbildung', 'Dienstlich', 'Klassenfahrt', 'Exkursion', 'Praktikum', 'Prüfung', 'Sonderurlaub', 'Mutterschutz/Elternzeit', 'Sperrung', 'Sonstiges'];

/** Übernimmt Portal-Meldungen (z. B. Krankmeldungen) als Abwesenheiten/Tagesnachrichten. */
export function uebernehmeMeldungen(d: SchoolData, meldungen: PortalMeldung[]): { uebernommen: string[]; abgelehnt: string[] } {
  const uebernommen: string[] = [];
  const abgelehnt: string[] = [];
  const maxStunde = d.zeitraster.stunden.length;
  for (const m of meldungen) {
    if (m.typ === 'abwesenheit') {
      const l = d.lehrer.find((x) => x.kuerzel.toLowerCase() === (m.lehrerKuerzel ?? m.absender).toLowerCase());
      if (!l || !m.von) {
        abgelehnt.push(m.id);
        continue;
      }
      const grund = GRUENDE.find((g) => g === m.grund) ?? 'Krankheit';
      d.abwesenheiten.push({
        id: newId('aw'),
        typ: 'lehrer',
        refId: l.id,
        von: m.von,
        bis: m.bis ?? m.von,
        stundeVon: m.stundeVon ?? 1,
        stundeBis: m.stundeBis ?? maxStunde,
        grund,
        text: `${m.text ?? ''} (Portal-Meldung ${m.absender})`.trim(),
        begleitLehrerIds: [],
      });
      uebernommen.push(m.id);
    } else if (m.typ === 'nachricht' && m.text) {
      d.tagesnachrichten.push({ id: newId('tn'), datum: m.von ?? todayISO(), text: `${m.text} – ${m.absender}`, intern: true });
      uebernommen.push(m.id);
    } else abgelehnt.push(m.id);
  }
  return { uebernommen, abgelehnt };
}
