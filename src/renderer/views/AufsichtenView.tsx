import { aufsichtenProLehrer, verteileAufsichten } from '../../core/aufsicht';
import { newId, TAGE_LANG } from '../../core/model';
import { api } from '../api';
import { MultiSelect, useConfirm } from '../components/ui';
import { aufsichtsplanHTML } from '../print';
import { useStore } from '../store';

export function AufsichtenView() {
  const { data, idx, update, toast } = useStore();
  const [dialog, confirm] = useConfirm();
  const pausen = [...data.pausen].sort((a, b) => a.nachStunde - b.nachStunde);
  const opts = data.lehrer.filter((l) => l.aktiv).map((l) => ({ value: l.id, label: l.kuerzel }));
  const proLehrer = aufsichtenProLehrer(data);

  const setZelle = (pauseId: string, bereichId: string, tag: number, ids: string[]) =>
    update('Aufsicht geändert', (d) => {
      d.aufsichten = d.aufsichten.filter((a) => !(a.pauseId === pauseId && a.bereichId === bereichId && a.tag === tag));
      for (const l of ids) d.aufsichten.push({ id: newId('au'), pauseId, bereichId, tag, lehrerId: l });
    });

  return (
    <>
      {dialog}
      <div className="card">
        <div className="row" style={{ marginBottom: 10 }}>
          <button className="primary" onClick={() => {
            if (!data.stundenplan.length) return toast('Bitte zuerst einen Stundenplan erstellen.', 'fehler');
            confirm('Alle Pausenaufsichten automatisch und fair (anteilig zum Deputat) neu verteilen? Bestehende Einteilungen werden ersetzt.', () => update('Aufsichten verteilt', (d) => { d.aufsichten = verteileAufsichten(d); }), 'Verteilen');
          }}>⚙️ Automatisch verteilen</button>
          <button onClick={() => api.printPdf(aufsichtsplanHTML(data), 'Aufsichtsplan.pdf', true)}>🖨️ PDF</button>
          <span className="muted small">Es werden nur Lehrkräfte eingeteilt, die vor oder nach der Pause Unterricht haben.</span>
        </div>
        <div className="tbl-wrap">
          <table className="tbl">
            <thead>
              <tr>
                <th>Pause</th><th>Bereich</th>
                {Array.from({ length: data.zeitraster.tage }, (_, t) => <th key={t}>{TAGE_LANG[t]}</th>)}
              </tr>
            </thead>
            <tbody>
              {pausen.flatMap((p) => data.aufsichtsbereiche.map((b) => (
                <tr key={p.id + b.id}>
                  <td className="nowrap">{p.name} <span className="muted small">(nach {p.nachStunde}.)</span></td>
                  <td>{b.name} <span className="muted small">×{b.bedarf}</span></td>
                  {Array.from({ length: data.zeitraster.tage }, (_, t) => {
                    const ids = data.aufsichten.filter((a) => a.pauseId === p.id && a.bereichId === b.id && a.tag === t).map((a) => a.lehrerId);
                    return (
                      <td key={t} style={{ background: ids.length < b.bedarf ? '#fff4e0' : undefined }}>
                        <MultiSelect options={opts} value={ids} onChange={(v) => setZelle(p.id, b.id, t, v)} placeholder="– frei –" />
                      </td>
                    );
                  })}
                </tr>
              )))}
            </tbody>
          </table>
        </div>
      </div>
      <div className="card">
        <h2>Aufsichten je Lehrkraft</h2>
        <div className="row">
          {data.lehrer.filter((l) => l.aktiv).sort((a, b) => (proLehrer.get(b.id) ?? 0) - (proLehrer.get(a.id) ?? 0)).map((l) => (
            <span key={l.id} className="chip">{idx.lehrer.get(l.id)?.kuerzel}: <b>{proLehrer.get(l.id) ?? 0}</b></span>
          ))}
        </div>
      </div>
    </>
  );
}
