import { useMemo, useState } from 'react';
import { newUnterricht } from '../../core/factory';
import { kurzLehrer } from '../../core/index';
import { loescheUnterrichte } from '../../core/loeschen';
import { sollStunden, type Unterricht } from '../../core/model';
import { MultiSelect, NumberInput, TextInput, useConfirm } from '../components/ui';
import { useStore } from '../store';

export function UnterrichtView() {
  const { data, idx, update } = useStore();
  const [fKlasse, setFKlasse] = useState('');
  const [fLehrer, setFLehrer] = useState('');
  const [fFach, setFFach] = useState('');
  const [nurOffen, setNurOffen] = useState(false);
  const [dialog, confirm] = useConfirm();
  const klassenOpts = useMemo(() => data.klassen.map((k) => ({ value: k.id, label: k.name })), [data.klassen]);
  const lehrerOpts = useMemo(() => data.lehrer.map((l) => ({ value: l.id, label: l.kuerzel })), [data.lehrer]);
  const raumOpts = useMemo(() => data.raeume.map((r) => ({ value: r.id, label: r.kuerzel })), [data.raeume]);
  const fachOpts = useMemo(() => [...data.faecher].sort((a, b) => a.kuerzel.localeCompare(b.kuerzel)), [data.faecher]);

  const rows = data.unterricht.filter(
    (u) =>
      (!fKlasse || u.klassenIds.includes(fKlasse)) &&
      (!fLehrer || u.lehrerIds.includes(fLehrer)) &&
      (!fFach || u.fachId === fFach) &&
      (!nurOffen || u.lehrerIds.length === 0),
  );
  const sorted = [...rows].sort((a, b) => {
    const ka = idx.klassen.get(a.klassenIds[0])?.name ?? '';
    const kb = idx.klassen.get(b.klassenIds[0])?.name ?? '';
    return ka.localeCompare(kb, 'de', { numeric: true }) || (idx.faecher.get(a.fachId)?.kuerzel ?? '').localeCompare(idx.faecher.get(b.fachId)?.kuerzel ?? '');
  });
  const set = (id: string, f: (u: Unterricht) => void) => update('Unterricht bearbeitet', (d) => { const u = d.unterricht.find((x) => x.id === id); if (u) f(u); });
  const summe = rows.reduce((s, u) => s + u.wochenstunden, 0);
  const lehrerSel = fLehrer ? idx.lehrer.get(fLehrer) : undefined;

  const ausStundentafel = () => {
    let n = 0;
    update('Unterricht aus Stundentafel', (d) => {
      for (const k of d.klassen) {
        for (const z of d.stundentafel.filter((x) => x.jahrgang === k.jahrgang)) {
          if (d.unterricht.some((u) => u.klassenIds.includes(k.id) && u.fachId === z.fachId)) continue;
          const fach = d.faecher.find((f) => f.id === z.fachId);
          const raumIds = fach?.raumTyp ? d.raeume.filter((r) => r.typ === fach.raumTyp).map((r) => r.id) : [];
          d.unterricht.push(newUnterricht({ fachId: z.fachId, klassenIds: [k.id], wochenstunden: z.stunden, raumIds }));
          n++;
        }
      }
    });
    alert(`${n} Unterrichte aus der Stundentafel angelegt. Lehrkräfte bitte zuordnen (Filter „ohne Lehrkraft“).`);
  };

  const autoZuordnen = () => {
    update('Lehrkräfte automatisch zugeordnet', (d) => {
      const last = new Map<string, number>();
      for (const u of d.unterricht) for (const l of u.lehrerIds) last.set(l, (last.get(l) ?? 0) + u.wochenstunden);
      for (const u of d.unterricht) {
        if (u.lehrerIds.length) continue;
        const cands = d.lehrer
          .filter((l) => l.aktiv && l.faecher.includes(u.fachId))
          .map((l) => ({ l, frei: sollStunden(l) - (last.get(l.id) ?? 0) }))
          .filter((c) => c.frei >= u.wochenstunden)
          .sort((a, b) => b.frei - a.frei);
        const c = cands[0];
        if (!c) continue;
        u.lehrerIds = [c.l.id];
        last.set(c.l.id, (last.get(c.l.id) ?? 0) + u.wochenstunden);
      }
    });
  };

  return (
    <div className="card">
      {dialog}
      <div className="row" style={{ marginBottom: 10 }}>
        <button className="primary" onClick={() => update('Unterricht angelegt', (d) => {
          d.unterricht.push(newUnterricht({ fachId: fFach || d.faecher[0]?.id || '', klassenIds: fKlasse ? [fKlasse] : [], lehrerIds: fLehrer ? [fLehrer] : [] }));
        })}>+ Unterricht</button>
        <button onClick={ausStundentafel} disabled={!data.stundentafel.length}>Aus Stundentafel erzeugen</button>
        <button onClick={autoZuordnen} title="Ordnet Unterrichten ohne Lehrkraft eine Fachlehrkraft mit freiem Deputat zu">Lehrkräfte vorschlagen</button>
        <span className="spacer" />
        <select value={fKlasse} onChange={(e) => setFKlasse(e.target.value)}>
          <option value="">Alle Klassen</option>
          {data.klassen.map((k) => <option key={k.id} value={k.id}>{k.name}</option>)}
        </select>
        <select value={fLehrer} onChange={(e) => setFLehrer(e.target.value)}>
          <option value="">Alle Lehrkräfte</option>
          {data.lehrer.map((l) => <option key={l.id} value={l.id}>{l.kuerzel}</option>)}
        </select>
        <select value={fFach} onChange={(e) => setFFach(e.target.value)}>
          <option value="">Alle Fächer</option>
          {fachOpts.map((f) => <option key={f.id} value={f.id}>{f.kuerzel}</option>)}
        </select>
        <label className="check"><input type="checkbox" checked={nurOffen} onChange={(e) => setNurOffen(e.target.checked)} /> ohne Lehrkraft</label>
      </div>
      <div className="row small muted" style={{ marginBottom: 8 }}>
        {rows.length} Unterrichte · {summe} Wochenstunden
        {lehrerSel && <> · {lehrerSel.kuerzel}: Soll {sollStunden(lehrerSel)} / Ist {summe}</>}
      </div>
      <div className="tbl-wrap">
        <table className="tbl">
          <thead>
            <tr>
              <th style={{ width: 90 }}>Fach</th>
              <th style={{ width: 130 }}>Klasse(n)</th>
              <th style={{ width: 130 }}>Lehrkraft</th>
              <th style={{ width: 60 }} title="Wochenstunden">Std.</th>
              <th style={{ width: 60 }} title="Anzahl Doppelstunden">Dopp.</th>
              <th style={{ width: 130 }}>Räume</th>
              <th style={{ width: 90 }}>Gruppe</th>
              <th style={{ width: 90 }} title="Parallel zu legender Unterricht (Band/Schiene)">Schiene</th>
              <th style={{ width: 80 }}>HJ</th>
              <th style={{ width: 70 }}>verplant</th>
              <th style={{ width: 70 }} />
            </tr>
          </thead>
          <tbody>
            {sorted.map((u) => {
              const verplant = idx.plaeneProUnterricht.get(u.id)?.length ?? 0;
              return (
                <tr key={u.id}>
                  <td>
                    <select value={u.fachId} onChange={(e) => set(u.id, (x) => { x.fachId = e.target.value; })}>
                      {fachOpts.map((f) => <option key={f.id} value={f.id}>{f.kuerzel}</option>)}
                    </select>
                  </td>
                  <td><MultiSelect options={klassenOpts} value={u.klassenIds} onChange={(v) => set(u.id, (x) => { x.klassenIds = v; })} /></td>
                  <td title={kurzLehrer(idx, u.lehrerIds)}><MultiSelect options={lehrerOpts} value={u.lehrerIds} placeholder="– offen –" onChange={(v) => set(u.id, (x) => { x.lehrerIds = v; })} /></td>
                  <td><NumberInput value={u.wochenstunden} min={0} step={1} onChange={(n) => set(u.id, (x) => { x.wochenstunden = n; })} /></td>
                  <td><NumberInput value={u.doppelstunden} min={0} max={Math.floor(u.wochenstunden / 2)} onChange={(n) => set(u.id, (x) => { x.doppelstunden = n; })} /></td>
                  <td><MultiSelect options={raumOpts} value={u.raumIds} placeholder="Stammraum" onChange={(v) => set(u.id, (x) => { x.raumIds = v; })} /></td>
                  <td><TextInput value={u.gruppe} onChange={(v) => set(u.id, (x) => { x.gruppe = v; })} /></td>
                  <td><TextInput value={u.schiene} onChange={(v) => set(u.id, (x) => { x.schiene = v; })} /></td>
                  <td>
                    <select value={u.halbjahr} onChange={(e) => set(u.id, (x) => { x.halbjahr = Number(e.target.value) as 0 | 1 | 2; })}>
                      <option value={0}>ganz</option>
                      <option value={1}>1. HJ</option>
                      <option value={2}>2. HJ</option>
                    </select>
                  </td>
                  <td><span className={`pill ${verplant === u.wochenstunden ? 'ok' : verplant ? 'warn' : ''}`}>{verplant}/{u.wochenstunden}</span></td>
                  <td className="nowrap">
                    <button className="sm" title="Duplizieren" onClick={() => update('Unterricht kopiert', (d) => { d.unterricht.push({ ...structuredClone(u), id: newUnterricht().id }); })}>⧉</button>{' '}
                    <button className="sm danger" onClick={() => confirm('Unterricht inkl. Planeinträgen löschen?', () => update('Unterricht gelöscht', (d) => loescheUnterrichte(d, new Set([u.id]))), 'Löschen')}>✕</button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      <p className="muted small">Kopplungen: mehrere Klassen bzw. Lehrkräfte in einer Zeile. Teilgruppen (z. B. KR/ER/PPL) erhalten unterschiedliche Gruppennamen und dieselbe Schiene – sie werden dann parallel gelegt.</p>
    </div>
  );
}
