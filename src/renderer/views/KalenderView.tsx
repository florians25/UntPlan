import { useState } from 'react';
import { addDays, formatDE, fromISO, toISO, weekdayIndex } from '../../core/dates';
import { newId, type FerienArt } from '../../core/model';
import { feiertageNRW, FERIEN_NRW } from '../../core/nrw';
import { freierTagAm } from '../../core/vertretung';
import { useStore } from '../store';

const ARTEN: FerienArt[] = ['Ferien', 'Feiertag', 'Beweglicher Ferientag', 'Unterrichtsfrei', 'Pädagogischer Tag'];
const MONATE = ['Jan', 'Feb', 'Mär', 'Apr', 'Mai', 'Jun', 'Jul', 'Aug', 'Sep', 'Okt', 'Nov', 'Dez'];

export function KalenderView() {
  const { data, update } = useStore();
  const [neu, setNeu] = useState({ name: 'Beweglicher Ferientag', von: '', bis: '', art: 'Beweglicher Ferientag' as FerienArt });
  const sorted = [...data.freieTage].sort((a, b) => a.von.localeCompare(b.von));
  const start = data.meta.schuljahrBeginn || '2026-08-01';
  const months = Array.from({ length: 12 }, (_, i) => {
    const d = fromISO(start);
    d.setDate(1);
    d.setMonth(d.getMonth() + i);
    return toISO(d);
  });
  const beweglich = data.freieTage.filter((f) => f.art === 'Beweglicher Ferientag' && f.von >= data.meta.schuljahrBeginn && f.von <= data.meta.schuljahrEnde).length;
  const ergaenzen = () =>
    update('NRW-Ferien/Feiertage ergänzt', (d) => {
      const y = Number(d.meta.schuljahrBeginn.slice(0, 4)) || 2026;
      for (const t of [...FERIEN_NRW, ...feiertageNRW(y), ...feiertageNRW(y + 1)])
        if (!d.freieTage.some((x) => x.von === t.von && x.name === t.name)) d.freieTage.push({ ...t, id: newId('ft') });
    });

  return (
    <div className="grid2" style={{ gridTemplateColumns: '1.1fr 1fr' }}>
      <div className="card">
        <h2>Ferien, Feiertage und unterrichtsfreie Tage</h2>
        <div className="row" style={{ marginBottom: 10 }}>
          <input placeholder="Bezeichnung" value={neu.name} onChange={(e) => setNeu({ ...neu, name: e.target.value })} />
          <input type="date" value={neu.von} onChange={(e) => setNeu({ ...neu, von: e.target.value, bis: neu.bis || e.target.value })} />
          <input type="date" value={neu.bis} onChange={(e) => setNeu({ ...neu, bis: e.target.value })} />
          <select value={neu.art} onChange={(e) => setNeu({ ...neu, art: e.target.value as FerienArt })}>{ARTEN.map((a) => <option key={a}>{a}</option>)}</select>
          <button className="primary" disabled={!neu.von} onClick={() => update('Freier Tag', (d) => { d.freieTage.push({ id: newId('ft'), ...neu, bis: neu.bis || neu.von }); })}>Hinzufügen</button>
        </div>
        <div className="row small" style={{ marginBottom: 8 }}>
          <span className={`pill ${beweglich > 4 ? 'err' : 'info'}`}>{beweglich} bewegliche Ferientage im Schuljahr</span>
          <button className="sm" onClick={ergaenzen}>NRW-Termine ergänzen</button>
        </div>
        <div className="tbl-wrap" style={{ maxHeight: 'calc(100vh - 290px)' }}>
          <table className="tbl">
            <thead><tr><th>Bezeichnung</th><th>von</th><th>bis</th><th>Art</th><th /></tr></thead>
            <tbody>
              {sorted.map((f) => (
                <tr key={f.id}>
                  <td>{f.name}</td>
                  <td>{formatDE(f.von)}</td>
                  <td>{f.bis !== f.von ? formatDE(f.bis) : ''}</td>
                  <td><span className={`pill ${f.art === 'Ferien' ? 'warn' : f.art === 'Feiertag' ? 'err' : 'info'}`}>{f.art}</span></td>
                  <td><button className="sm danger" onClick={() => update('Freier Tag gelöscht', (d) => { d.freieTage = d.freieTage.filter((x) => x.id !== f.id); })}>✕</button></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <p className="muted small">Ferientermine gemäß Ferienordnung NRW. Bewegliche Ferientage legt die Schulkonferenz fest.</p>
      </div>
      <div className="card">
        <h2>Jahreskalender {data.meta.schuljahr}</h2>
        <div className="grid3" style={{ gap: 12 }}>
          {months.map((m) => {
            const first = m;
            const offset = weekdayIndex(first);
            const days: string[] = [];
            let x = first;
            while (x.slice(0, 7) === first.slice(0, 7)) {
              days.push(x);
              x = addDays(x, 1);
            }
            return (
              <div key={m}>
                <b className="small">{MONATE[Number(m.slice(5, 7)) - 1]} {m.slice(0, 4)}</b>
                <div className="cal">
                  {['M', 'D', 'M', 'D', 'F', 'S', 'S'].map((h, i) => <div key={i} className="hd">{h}</div>)}
                  {Array.from({ length: offset }, (_, i) => <div key={'e' + i} />)}
                  {days.map((dd) => {
                    const ft = freierTagAm(data, dd);
                    const we = weekdayIndex(dd) >= data.zeitraster.tage;
                    return <div key={dd} title={ft?.name} className={ft ? (ft.art === 'Ferien' ? 'fe' : 'ft') : we ? 'we' : ''}>{Number(dd.slice(8))}</div>;
                  })}
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}
