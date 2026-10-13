import { useState } from 'react';
import { newRaum } from '../../core/factory';
import { loescheRaum, verwendung } from '../../core/loeschen';
import type { Raum, RaumTyp } from '../../core/model';
import { Modal, NumberInput, Search, SlotGrid, TextInput, useConfirm, useSort } from '../components/ui';
import { useStore } from '../store';
import { RAUMTYPEN } from './FaecherView';

export function RaeumeView() {
  const { data, update, setView } = useStore();
  const [q, setQ] = useState('');
  const [sperr, setSperr] = useState<string | null>(null);
  const [dialog, confirm] = useConfirm();
  const rows = data.raeume.filter((r) => `${r.kuerzel} ${r.name}`.toLowerCase().includes(q.toLowerCase()));
  const { sorted, th } = useSort(rows, { k: (r) => r.kuerzel, t: (r) => r.typ, c: (r) => r.kapazitaet }, 'k');
  const set = (id: string, fn: (r: Raum) => void) => update('Raum bearbeitet', (d) => { const r = d.raeume.find((x) => x.id === id); if (r) fn(r); });
  const belegung = (id: string) => data.stundenplan.filter((p) => p.raumId === id).length;
  const kapazitaet = data.zeitraster.tage * data.zeitraster.stunden.length;
  const editing = data.raeume.find((r) => r.id === sperr);
  return (
    <div className="card">
      {dialog}
      <div className="row" style={{ marginBottom: 10 }}>
        <button className="primary" onClick={() => update('Raum angelegt', (d) => { d.raeume.push(newRaum({ kuerzel: 'R', name: 'Neuer Raum' })); })}>+ Raum</button>
        <Search value={q} onChange={setQ} />
      </div>
      <div className="tbl-wrap">
        <table className="tbl">
          <thead>
            <tr>
              {th('k', 'Kürzel', { width: 100 })}
              <th>Bezeichnung</th>
              {th('t', 'Typ', { width: 160 })}
              {th('c', 'Plätze', { width: 80 })}
              <th style={{ width: 160 }}>Auslastung</th>
              <th style={{ width: 200 }} />
            </tr>
          </thead>
          <tbody>
            {sorted.map((r) => {
              const b = belegung(r.id);
              return (
                <tr key={r.id}>
                  <td><TextInput value={r.kuerzel} onChange={(v) => set(r.id, (x) => { x.kuerzel = v; })} /></td>
                  <td><TextInput value={r.name} onChange={(v) => set(r.id, (x) => { x.name = v; })} /></td>
                  <td>
                    <select value={r.typ} onChange={(e) => set(r.id, (x) => { x.typ = e.target.value as RaumTyp; })}>
                      {RAUMTYPEN.map((t) => <option key={t}>{t}</option>)}
                    </select>
                  </td>
                  <td><NumberInput value={r.kapazitaet} min={0} onChange={(n) => set(r.id, (x) => { x.kapazitaet = n; })} /></td>
                  <td>
                    <div className="bar" title={`${b} von ${kapazitaet} Stunden`}><div style={{ width: `${(b / kapazitaet) * 100}%` }} /></div>
                    <span className="small muted">{b} / {kapazitaet}</span>
                  </td>
                  <td className="nowrap">
                    <button className="sm" onClick={() => setSperr(r.id)}>Sperrzeiten{r.sperrzeiten.length ? ` (${r.sperrzeiten.length})` : ''}</button>{' '}
                    <button className="sm" onClick={() => setView('stundenplan', `raum:${r.id}`)}>Plan</button>{' '}
                    <button className="sm danger" onClick={() => confirm(<>Raum <b>{r.kuerzel}</b> löschen? {verwendung(data, 'raum', r.id)} Planeinträge verlieren den Raum.</>, () => update('Raum gelöscht', (d) => loescheRaum(d, r.id)), 'Löschen')}>✕</button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      {editing && (
        <Modal title={`Sperrzeiten Raum ${editing.kuerzel}`} onClose={() => setSperr(null)} footer={<button className="primary" onClick={() => setSperr(null)}>Fertig</button>}>
          <SlotGrid tage={data.zeitraster.tage} stunden={data.zeitraster.stunden.length} value={editing.sperrzeiten} onChange={(v) => set(editing.id, (x) => { x.sperrzeiten = v; })} />
        </Modal>
      )}
    </div>
  );
}
