import { useState } from 'react';
import { newFach } from '../../core/factory';
import { loescheFach, verwendung } from '../../core/loeschen';
import type { Fach, RaumTyp } from '../../core/model';
import { NumberInput, Search, TextInput, useConfirm, useSort } from '../components/ui';
import { useStore } from '../store';

export const RAUMTYPEN: RaumTyp[] = ['Klassenraum', 'Fachraum', 'Sporthalle', 'Computerraum', 'Musikraum', 'Kunstraum', 'Sonstiges'];

export function FaecherView() {
  const { data, update } = useStore();
  const [q, setQ] = useState('');
  const [dialog, confirm] = useConfirm();
  const rows = data.faecher.filter((f) => `${f.kuerzel} ${f.name}`.toLowerCase().includes(q.toLowerCase()));
  const { sorted, th } = useSort(rows, { k: (f) => f.kuerzel, n: (f) => f.name }, 'k');
  const set = (id: string, fn: (f: Fach) => void) => update('Fach bearbeitet', (d) => { const f = d.faecher.find((x) => x.id === id); if (f) fn(f); });
  return (
    <div className="card">
      {dialog}
      <div className="row" style={{ marginBottom: 10 }}>
        <button className="primary" onClick={() => update('Fach angelegt', (d) => { d.faecher.push(newFach({ kuerzel: 'NEU', name: 'Neues Fach' })); })}>+ Fach</button>
        <Search value={q} onChange={setQ} />
        <span className="muted small">Kürzel entsprechen dem in NRW (Schild-NRW) üblichen Fächerkatalog.</span>
      </div>
      <div className="tbl-wrap">
        <table className="tbl">
          <thead>
            <tr>
              {th('k', 'Kürzel', { width: 90 })}
              {th('n', 'Bezeichnung')}
              <th style={{ width: 60 }}>Farbe</th>
              <th style={{ width: 160 }}>Benötigter Raum</th>
              <th style={{ width: 90 }}>Hauptfach</th>
              <th style={{ width: 110 }}>max. Std./Tag</th>
              <th style={{ width: 90 }}>Verwendet</th>
              <th style={{ width: 50 }} />
            </tr>
          </thead>
          <tbody>
            {sorted.map((f) => (
              <tr key={f.id}>
                <td><TextInput value={f.kuerzel} onChange={(v) => set(f.id, (x) => { x.kuerzel = v; })} /></td>
                <td><TextInput value={f.name} onChange={(v) => set(f.id, (x) => { x.name = v; })} /></td>
                <td><input type="color" value={f.farbe} onChange={(e) => set(f.id, (x) => { x.farbe = e.target.value; })} /></td>
                <td>
                  <select value={f.raumTyp ?? ''} onChange={(e) => set(f.id, (x) => { x.raumTyp = (e.target.value || null) as RaumTyp | null; })}>
                    <option value="">beliebig / Stammraum</option>
                    {RAUMTYPEN.map((t) => <option key={t}>{t}</option>)}
                  </select>
                </td>
                <td><input type="checkbox" checked={f.hauptfach} onChange={(e) => set(f.id, (x) => { x.hauptfach = e.target.checked; })} /></td>
                <td><NumberInput value={f.maxProTag} min={1} onChange={(n) => set(f.id, (x) => { x.maxProTag = n; })} /></td>
                <td className="muted">{verwendung(data, 'fach', f.id) || ''}</td>
                <td><button className="sm danger" onClick={() => confirm(<>Fach <b>{f.kuerzel}</b> löschen? {verwendung(data, 'fach', f.id)} Unterricht(e) werden mitgelöscht.</>, () => update('Fach gelöscht', (d) => loescheFach(d, f.id)), 'Löschen')}>✕</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
