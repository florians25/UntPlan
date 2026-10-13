import { useMemo, useState } from 'react';
import { JAHRGAENGE } from '../../core/nrw';
import { stundentafelAbgleich } from '../../core/statistik';
import { NumberInput } from '../components/ui';
import { useStore } from '../store';

export function StundentafelView() {
  const { data, update } = useStore();
  const jgAlle = useMemo(() => [...new Set([...JAHRGAENGE[data.meta.schulform], ...data.klassen.map((k) => k.jahrgang)])], [data.meta.schulform, data.klassen]);
  const [nurBenutzt, setNurBenutzt] = useState(true);
  const faecher = [...data.faecher]
    .filter((f) => !nurBenutzt || data.stundentafel.some((z) => z.fachId === f.id) || data.unterricht.some((u) => u.fachId === f.id))
    .sort((a, b) => a.kuerzel.localeCompare(b.kuerzel));
  const wert = (jg: string, fid: string) => data.stundentafel.find((z) => z.jahrgang === jg && z.fachId === fid)?.stunden ?? 0;
  const setWert = (jg: string, fid: string, n: number) =>
    update('Stundentafel', (d) => {
      const z = d.stundentafel.find((x) => x.jahrgang === jg && x.fachId === fid);
      if (n <= 0) d.stundentafel = d.stundentafel.filter((x) => x !== z);
      else if (z) z.stunden = n;
      else d.stundentafel.push({ jahrgang: jg, fachId: fid, stunden: n });
    });
  const abgleich = useMemo(() => stundentafelAbgleich(data), [data]);
  return (
    <>
      <div className="card">
        <div className="row" style={{ marginBottom: 10 }}>
          <label className="check"><input type="checkbox" checked={nurBenutzt} onChange={(e) => setNurBenutzt(e.target.checked)} /> nur verwendete Fächer</label>
          <span className="muted small">Wochenstunden je Jahrgang nach APO-SI/APO-GOSt bzw. schulinternem Stundenkonzept. Die Werte sind schulindividuell zu pflegen.</span>
        </div>
        <div className="tbl-wrap">
          <table className="tbl">
            <thead>
              <tr>
                <th>Fach</th>
                {jgAlle.map((j) => <th key={j} style={{ width: 64, textAlign: 'center' }}>{j}</th>)}
              </tr>
            </thead>
            <tbody>
              {faecher.map((f) => (
                <tr key={f.id}>
                  <td><span className="chip" style={{ background: f.farbe }}>{f.kuerzel}</span> {f.name}</td>
                  {jgAlle.map((j) => (
                    <td key={j}><NumberInput value={wert(j, f.id)} min={0} onChange={(n) => setWert(j, f.id, n)} style={{ textAlign: 'center' }} /></td>
                  ))}
                </tr>
              ))}
              <tr>
                <td><b>Summe</b></td>
                {jgAlle.map((j) => <td key={j} style={{ textAlign: 'center' }}><b>{data.stundentafel.filter((z) => z.jahrgang === j).reduce((s, z) => s + z.stunden, 0)}</b></td>)}
              </tr>
            </tbody>
          </table>
        </div>
      </div>
      <div className="card">
        <h2>Abgleich Stundentafel ↔ Unterrichtsverteilung</h2>
        {abgleich.length === 0 ? <p className="muted">Alle Klassen entsprechen der Stundentafel.</p> : (
          <table className="tbl" style={{ maxWidth: 600 }}>
            <thead><tr><th>Klasse</th><th>Fach</th><th>Soll</th><th>Ist</th><th>Differenz</th></tr></thead>
            <tbody>
              {abgleich.map((a, i) => (
                <tr key={i}><td>{a.klasse}</td><td>{a.fach}</td><td>{a.soll}</td><td>{a.ist}</td><td><span className={`pill ${a.ist < a.soll ? 'err' : 'warn'}`}>{a.ist - a.soll > 0 ? '+' : ''}{a.ist - a.soll}</span></td></tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </>
  );
}
