import { useMemo, useState } from 'react';
import { pruefeStundenplan, type Schwere } from '../../core/pruefung';
import { useStore } from '../store';

export function DiagnoseView() {
  const { data, idx, setView } = useStore();
  const befunde = useMemo(() => pruefeStundenplan(data, idx), [data, idx]);
  const [filter, setFilter] = useState<Schwere | ''>('');
  const [kat, setKat] = useState('');
  const kategorien = [...new Set(befunde.map((b) => b.kategorie))];
  const rows = befunde.filter((b) => (!filter || b.schwere === filter) && (!kat || b.kategorie === kat));
  const count = (s: Schwere) => befunde.filter((b) => b.schwere === s).length;
  const gehe = (b: (typeof befunde)[number]) => {
    if (!b.ref) return;
    if (b.ref.typ === 'unterricht') setView('unterricht');
    else setView('stundenplan', `${b.ref.typ}:${b.ref.id}`);
  };
  return (
    <div className="card">
      <div className="row" style={{ marginBottom: 12 }}>
        <button className={filter === '' ? 'primary' : ''} onClick={() => setFilter('')}>Alle ({befunde.length})</button>
        <button className={filter === 'fehler' ? 'primary' : ''} onClick={() => setFilter('fehler')}>Konflikte ({count('fehler')})</button>
        <button className={filter === 'warnung' ? 'primary' : ''} onClick={() => setFilter('warnung')}>Warnungen ({count('warnung')})</button>
        <button className={filter === 'hinweis' ? 'primary' : ''} onClick={() => setFilter('hinweis')}>Hinweise ({count('hinweis')})</button>
        <select value={kat} onChange={(e) => setKat(e.target.value)}>
          <option value="">Alle Kategorien</option>
          {kategorien.map((k) => <option key={k}>{k}</option>)}
        </select>
      </div>
      {rows.length === 0 && <div className="empty">✔ Keine Befunde</div>}
      {rows.map((b, i) => (
        <div key={i} className="befund">
          <span className={`pill ${b.schwere === 'fehler' ? 'err' : b.schwere === 'warnung' ? 'warn' : 'info'}`} style={{ minWidth: 130 }}>{b.kategorie}</span>
          <span style={{ flex: 1 }}>{b.text}</span>
          {b.ref && <button className="sm" onClick={() => gehe(b)}>Anzeigen</button>}
        </div>
      ))}
    </div>
  );
}
