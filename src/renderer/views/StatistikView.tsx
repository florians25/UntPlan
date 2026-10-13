import { useMemo, useState } from 'react';
import { addDays, todayISO } from '../../core/dates';
import { toCSV } from '../../core/austausch';
import { ausfallStatistik, deputatsUebersicht, mehrarbeitsStatistik } from '../../core/statistik';
import { monatVon } from '../../core/vertretung';
import { api } from '../api';
import { useSort } from '../components/ui';
import { useStore } from '../store';

export function StatistikView() {
  const [tab, setTab] = useState('deputat');
  return (
    <>
      <div className="tabs">
        {[['deputat', 'Deputat / Unterrichtsverteilung'], ['mehrarbeit', 'Mehrarbeit'], ['ausfall', 'Vertretung & Ausfall']].map(([k, l]) => (
          <button key={k} className={`tab ${tab === k ? 'active' : ''}`} onClick={() => setTab(k)}>{l}</button>
        ))}
      </div>
      {tab === 'deputat' && <Deputat />}
      {tab === 'mehrarbeit' && <Mehrarbeit />}
      {tab === 'ausfall' && <Ausfall />}
    </>
  );
}

function Deputat() {
  const { data } = useStore();
  const [hj, setHj] = useState<1 | 2>(1);
  const rows = useMemo(() => deputatsUebersicht(data, hj), [data, hj]);
  const { sorted, th } = useSort(rows, { k: (r) => r.kuerzel, soll: (r) => r.soll, ist: (r) => r.ist, diff: (r) => r.differenz }, 'k');
  const sum = (f: (r: (typeof rows)[number]) => number) => rows.reduce((s, r) => s + f(r), 0);
  const exp = () => api.exportFile('Deputat.csv', toCSV([['Kürzel', 'Name', 'Pflicht', 'Anrechnung', 'Soll', 'Ist', 'Verplant', 'Differenz'], ...rows.map((r) => [r.kuerzel, r.name, r.pflicht, r.anrechnung, r.soll, r.ist, r.verplant, r.differenz].map((x) => String(x).replace('.', ',')))]), 'CSV', 'csv');
  return (
    <div className="card">
      <div className="row" style={{ marginBottom: 10 }}>
        <select value={hj} onChange={(e) => setHj(Number(e.target.value) as 1 | 2)}><option value={1}>1. Halbjahr</option><option value={2}>2. Halbjahr</option></select>
        <span className="muted small">Soll {sum((r) => r.soll)} · Ist {sum((r) => r.ist)} · Differenz {sum((r) => r.differenz)}</span>
        <span className="spacer" />
        <button onClick={exp}>CSV-Export</button>
      </div>
      <div className="tbl-wrap">
        <table className="tbl">
          <thead><tr>{th('k', 'Kürzel')}<th>Name</th><th>Pflicht</th><th>Anrechnung</th>{th('soll', 'Soll')}{th('ist', 'Ist (UV)')}<th>Verplant</th>{th('diff', 'Differenz')}</tr></thead>
          <tbody>
            {sorted.map((r) => (
              <tr key={r.lehrerId}>
                <td><b>{r.kuerzel}</b></td><td>{r.name}</td><td>{r.pflicht}</td><td>{r.anrechnung || ''}</td><td>{r.soll}</td><td>{r.ist}</td><td>{r.verplant}</td>
                <td><span className={`pill ${r.differenz === 0 ? 'ok' : r.differenz > 0 ? 'warn' : 'err'}`}>{r.differenz > 0 ? '+' : ''}{r.differenz}</span></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function Mehrarbeit() {
  const { data } = useStore();
  const [monat, setMonat] = useState(monatVon(todayISO()));
  const rows = useMemo(() => mehrarbeitsStatistik(data, monat), [data, monat]);
  const exp = () => api.exportFile(`Mehrarbeit_${monat}.csv`, toCSV([['Kürzel', 'Monat', 'Vertretungen', 'Mehrarbeit', 'Minderarbeit', 'Saldo', 'vergütungsfähig'], ...rows.map((r) => [r.kuerzel, r.monat, r.vertretungen, r.mehrarbeit, r.minderarbeit, r.saldo, r.verguetungsfaehig])]), 'CSV', 'csv');
  return (
    <div className="card">
      <div className="row" style={{ marginBottom: 10 }}>
        <input type="month" value={monat} onChange={(e) => setMonat(e.target.value)} />
        <span className="muted small">Vergütungsfähig, wenn im Kalendermonat mehr als {data.einstellungen.mehrarbeitsGrenze} Stunden Mehrarbeit geleistet wurden (dann alle Stunden). Einstellbar unter „Schule › Einstellungen“.</span>
        <span className="spacer" />
        <button onClick={exp}>CSV-Export (Abrechnung)</button>
      </div>
      <table className="tbl">
        <thead><tr><th>Kürzel</th><th>Vertretungen</th><th>davon Mehrarbeit</th><th>Minderarbeit (Freisetzung)</th><th>Saldo</th><th>vergütungsfähig</th></tr></thead>
        <tbody>
          {rows.map((r) => (
            <tr key={r.lehrerId}>
              <td><b>{r.kuerzel}</b></td><td>{r.vertretungen}</td><td>{r.mehrarbeit}</td><td>{r.minderarbeit}</td><td>{r.saldo}</td>
              <td>{r.verguetungsfaehig ? <span className="pill warn">{r.verguetungsfaehig} Std.</span> : <span className="muted">–</span>}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {!rows.length && <div className="empty">Keine Vertretungen in diesem Monat.</div>}
      <p className="muted small">Hinweis: Die Auswertung ist eine Arbeitshilfe. Maßgeblich sind die jeweils gültigen Regelungen des Landes NRW zur Mehrarbeit (insbesondere für Teilzeitkräfte).</p>
    </div>
  );
}

function Ausfall() {
  const { data } = useStore();
  const [von, setVon] = useState(addDays(todayISO(), -30));
  const [bis, setBis] = useState(todayISO());
  const s = useMemo(() => ausfallStatistik(data, von, bis), [data, von, bis]);
  const box = (l: string, v: number | string) => <div className="card stat"><div className="v">{v}</div><div className="l">{l}</div></div>;
  return (
    <>
      <div className="card row">
        Zeitraum <input type="date" value={von} onChange={(e) => setVon(e.target.value)} /> bis <input type="date" value={bis} onChange={(e) => setBis(e.target.value)} />
      </div>
      <div className="grid4">
        {box('Regelungen gesamt', s.gesamt)}
        {box('vertreten / mitbetreut', s.vertreten)}
        {box('Entfall', s.entfall)}
        {box('EVA', s.eva)}
        {box('Freisetzungen', s.freisetzung)}
        {box('Raumänderungen', s.raumaenderung)}
        {box('Ausfallquote', `${(s.quoteAusfall * 100).toFixed(1)} %`)}
      </div>
    </>
  );
}
