import { useMemo, useState } from 'react';
import { newKlasse } from '../../core/factory';
import { loescheKlasse, verwendung } from '../../core/loeschen';
import type { Klasse } from '../../core/model';
import { JAHRGAENGE } from '../../core/nrw';
import { Modal, MultiSelect, NumberInput, Search, SlotGrid, TextInput, useConfirm, useSort } from '../components/ui';
import { useStore } from '../store';

export function KlassenView() {
  const { data, update, setView } = useStore();
  const [q, setQ] = useState('');
  const [sperr, setSperr] = useState<string | null>(null);
  const [dialog, confirm] = useConfirm();
  const lehrerOpts = useMemo(() => data.lehrer.map((l) => ({ value: l.id, label: `${l.kuerzel} – ${l.nachname}` })), [data.lehrer]);
  const raumOpts = data.raeume.map((r) => ({ value: r.id, label: r.kuerzel }));
  const jg = JAHRGAENGE[data.meta.schulform];
  const rows = data.klassen.filter((k) => k.name.toLowerCase().includes(q.toLowerCase()));
  const { sorted, th } = useSort(rows, { name: (k) => k.name, jg: (k) => k.jahrgang }, 'name');
  const set = (id: string, f: (k: Klasse) => void) => update('Klasse bearbeitet', (d) => { const k = d.klassen.find((x) => x.id === id); if (k) f(k); });
  const stunden = (id: string) => data.unterricht.filter((u) => u.klassenIds.includes(id)).reduce((s, u) => s + (u.gruppe ? 0 : u.wochenstunden), 0);

  const [serieOffen, setSerieOffen] = useState(false);
  const [anzahl, setAnzahl] = useState(3);
  const serie = () => {
    setSerieOffen(false);
    if (!anzahl) return;
    update('Klassen angelegt', (d) => {
      for (const j of jg) {
        if (['EF', 'Q1', 'Q2'].includes(j)) {
          if (!d.klassen.some((k) => k.name === j)) d.klassen.push(newKlasse({ name: j, jahrgang: j, keineHohlstunden: false, schuelerzahl: 90 }));
          continue;
        }
        for (let i = 0; i < anzahl; i++) {
          const name = `${j}${String.fromCharCode(97 + i)}`;
          if (!d.klassen.some((k) => k.name === name)) d.klassen.push(newKlasse({ name, jahrgang: j }));
        }
      }
    });
  };

  const editing = data.klassen.find((k) => k.id === sperr);
  return (
    <div className="card">
      {dialog}
      <div className="row" style={{ marginBottom: 10 }}>
        <button className="primary" onClick={() => update('Klasse angelegt', (d) => { d.klassen.push(newKlasse({ name: 'Neu', jahrgang: jg[0] })); })}>+ Klasse</button>
        <button onClick={() => setSerieOffen(true)}>Klassen für alle Jahrgänge anlegen…</button>
        <Search value={q} onChange={setQ} />
      </div>
      <div className="tbl-wrap">
        <table className="tbl">
          <thead>
            <tr>
              {th('name', 'Klasse', { width: 100 })}
              {th('jg', 'Jahrgang', { width: 100 })}
              <th>Klassenleitung</th>
              <th style={{ width: 110 }}>Stammraum</th>
              <th style={{ width: 80 }}>Schüler</th>
              <th style={{ width: 90 }}>Wochenstd.</th>
              <th style={{ width: 110 }} title="Hohlstunden im Klassenplan vermeiden">ohne Hohlstd.</th>
              <th style={{ width: 170 }} />
            </tr>
          </thead>
          <tbody>
            {sorted.map((k) => (
              <tr key={k.id}>
                <td><TextInput value={k.name} onChange={(v) => set(k.id, (x) => { x.name = v; })} /></td>
                <td>
                  <select value={k.jahrgang} onChange={(e) => set(k.id, (x) => { x.jahrgang = e.target.value; })}>
                    {[...new Set([...jg, k.jahrgang])].map((j) => <option key={j}>{j}</option>)}
                  </select>
                </td>
                <td><MultiSelect options={lehrerOpts} value={k.klassenlehrerIds} onChange={(v) => set(k.id, (x) => { x.klassenlehrerIds = v; })} /></td>
                <td><MultiSelect single options={[{ value: '', label: '–' }, ...raumOpts]} value={[k.raumId ?? '']} onChange={(v) => set(k.id, (x) => { x.raumId = v[0] || null; })} /></td>
                <td><NumberInput value={k.schuelerzahl} min={0} onChange={(n) => set(k.id, (x) => { x.schuelerzahl = n; })} /></td>
                <td>{stunden(k.id)}</td>
                <td><input type="checkbox" checked={k.keineHohlstunden} onChange={(e) => set(k.id, (x) => { x.keineHohlstunden = e.target.checked; })} /></td>
                <td className="nowrap">
                  <button className="sm" onClick={() => setSperr(k.id)}>Sperrzeiten{k.sperrzeiten.length ? ` (${k.sperrzeiten.length})` : ''}</button>{' '}
                  <button className="sm" onClick={() => setView('stundenplan', `klasse:${k.id}`)}>Plan</button>{' '}
                  <button className="sm danger" onClick={() => confirm(<>Klasse <b>{k.name}</b> löschen? {verwendung(data, 'klasse', k.id)} Unterricht(e) sind betroffen.</>, () => update('Klasse gelöscht', (d) => loescheKlasse(d, k.id)), 'Löschen')}>✕</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {serieOffen && (
        <Modal title="Klassen für alle Jahrgänge anlegen" onClose={() => setSerieOffen(false)} footer={<><button onClick={() => setSerieOffen(false)}>Abbrechen</button><button className="primary" onClick={serie}>Anlegen</button></>}>
          <label className="field">Parallelklassen je Jahrgang (a, b, c …)<NumberInput value={anzahl} min={1} max={12} onChange={setAnzahl} /></label>
          <p className="muted small">Jahrgänge: {jg.join(', ')}. Oberstufen-Jahrgänge (EF, Q1, Q2) werden als je eine Stufe angelegt. Bestehende Klassen bleiben erhalten.</p>
        </Modal>
      )}
      {editing && (
        <Modal title={`Sperrzeiten Klasse ${editing.name}`} onClose={() => setSperr(null)} footer={<button className="primary" onClick={() => setSperr(null)}>Fertig</button>}>
          <SlotGrid tage={data.zeitraster.tage} stunden={data.zeitraster.stunden.length} value={editing.sperrzeiten} onChange={(v) => set(editing.id, (x) => { x.sperrzeiten = v; })} />
          <p className="muted small">z. B. Nachmittage ohne Unterricht, Schwimmzeiten außer Haus.</p>
        </Modal>
      )}
    </div>
  );
}
