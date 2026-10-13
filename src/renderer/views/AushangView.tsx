import { useEffect, useMemo, useState } from 'react';
import { formatDE, todayISO, weekdayIndex } from '../../core/dates';
import { TAGE_LANG } from '../../core/model';
import { istUnterrichtstag, naechsterUnterrichtstag, vertretungsplanZeilen } from '../../core/vertretung';
import { exportVertretungsplanCSV } from '../../core/austausch';
import { api } from '../api';
import { vertretungsplanHTML } from '../print';
import { useStore } from '../store';

export function AushangView() {
  const { data, idx } = useStore();
  const start = istUnterrichtstag(data, todayISO()) ? todayISO() : naechsterUnterrichtstag(data, todayISO());
  const [datum, setDatum] = useState(start);
  const [tage, setTage] = useState(2);
  const [intern, setIntern] = useState(false);
  const [monitor, setMonitor] = useState(false);
  const [klasse, setKlasse] = useState('');
  const daten = useMemo(() => {
    const out = [datum];
    while (out.length < tage) out.push(naechsterUnterrichtstag(data, out[out.length - 1]));
    return out;
  }, [data, datum, tage]);

  // Monitor: automatische Aktualisierung der Uhrzeit
  const [jetzt, setJetzt] = useState(new Date());
  useEffect(() => {
    if (!monitor) return;
    const t = setInterval(() => setJetzt(new Date()), 30_000);
    const esc = (e: KeyboardEvent) => e.key === 'Escape' && setMonitor(false);
    window.addEventListener('keydown', esc);
    return () => { clearInterval(t); window.removeEventListener('keydown', esc); };
  }, [monitor]);

  const inhalt = (
    <>
      {daten.map((iso) => {
        const zeilen = vertretungsplanZeilen(data, iso, idx).filter((z) => !klasse || z.klassenIds.includes(klasse));
        const msgs = data.tagesnachrichten.filter((n) => n.datum === iso && (intern || !n.intern));
        return (
          <div key={iso} style={{ marginBottom: 20 }}>
            <h2 style={{ fontSize: monitor ? 26 : 16 }}>{TAGE_LANG[weekdayIndex(iso)]}, {formatDE(iso)}</h2>
            {msgs.map((m) => <div key={m.id} className="pill warn" style={{ display: 'block', marginBottom: 6, fontSize: monitor ? 18 : 13 }}>{m.text}</div>)}
            <table className={monitor ? '' : 'tbl'}>
              <thead><tr><th>Klasse</th><th>Std.</th><th>Fach</th>{intern && <th>Lehrkraft</th>}<th>Vertretung</th><th>Raum</th><th>Art</th><th>Hinweis</th></tr></thead>
              <tbody>
                {zeilen.map((z, i) => (
                  <tr key={i} className={['Entfall', 'Freisetzung'].includes(z.art) ? 'entfall' : ''}>
                    <td><b>{z.klassen}</b></td><td>{z.stunde}</td><td>{z.fachNeu}{z.fachNeu !== z.fach ? <span className="muted"> ({z.fach})</span> : null}</td>
                    {intern && <td>{z.lehrer}</td>}<td>{z.vertreter}</td><td>{z.raumNeu}</td>
                    <td><span className={monitor ? '' : `pill ${['Entfall', 'Freisetzung'].includes(z.art) ? 'err' : z.art === 'Raumänderung' ? 'info' : 'ok'}`}>{z.art}</span></td>
                    <td>{z.text}</td>
                  </tr>
                ))}
                {!zeilen.length && <tr><td colSpan={8} className="muted">Keine Vertretungen.</td></tr>}
              </tbody>
            </table>
          </div>
        );
      })}
    </>
  );

  if (monitor)
    return (
      <div className="monitor" style={{ position: 'fixed', inset: 0, zIndex: 60, overflow: 'auto', borderRadius: 0 }} onDoubleClick={() => setMonitor(false)}>
        <div className="row" style={{ marginBottom: 12 }}>
          <span style={{ fontSize: 30, fontWeight: 700 }}>{data.einstellungen.vertretungsplanTitel}</span>
          <span className="spacer" />
          <span style={{ fontSize: 22 }}>{data.meta.schulname} · {jetzt.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })}</span>
        </div>
        {inhalt}
        <div className="small" style={{ color: '#5b6b8c' }}>ESC oder Doppelklick beendet die Monitoransicht.</div>
      </div>
    );

  return (
    <div className="card">
      <div className="row" style={{ marginBottom: 12 }}>
        <input type="date" value={datum} onChange={(e) => setDatum(e.target.value)} />
        <select value={tage} onChange={(e) => setTage(Number(e.target.value))}>
          {[1, 2, 3, 5].map((n) => <option key={n} value={n}>{n} Tag(e)</option>)}
        </select>
        <select value={klasse} onChange={(e) => setKlasse(e.target.value)}>
          <option value="">alle Klassen</option>
          {data.klassen.map((k) => <option key={k.id} value={k.id}>{k.name}</option>)}
        </select>
        <label className="check"><input type="checkbox" checked={intern} onChange={(e) => setIntern(e.target.checked)} /> Lehrerversion (mit abwesenden Lehrkräften)</label>
        <span className="spacer" />
        <button onClick={() => setMonitor(true)}>📺 Monitoransicht</button>
        <button onClick={() => api.printPdf(vertretungsplanHTML(data, daten, intern), `Vertretungsplan_${datum}.pdf`, false)}>🖨️ PDF</button>
        <button onClick={() => api.printHtml(vertretungsplanHTML(data, daten, intern))}>Drucken</button>
        <button onClick={() => api.exportFile(`Vertretungsplan_${datum}.csv`, exportVertretungsplanCSV(data, datum), 'CSV', 'csv')}>CSV</button>
      </div>
      {inhalt}
    </div>
  );
}
